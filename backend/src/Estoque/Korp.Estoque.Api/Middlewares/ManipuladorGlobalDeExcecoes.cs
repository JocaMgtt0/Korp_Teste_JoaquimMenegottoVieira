using Korp.Estoque.Domain.Excecoes;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Korp.Estoque.Api.Middlewares;

/// <summary>
/// Ponto unico de traducao de excecao em resposta HTTP.
///
/// Nenhum controller deste servico tem try/catch. A regra e simples: o dominio
/// lanca excecao tipada quando uma invariante e violada, e este manipulador
/// decide o status HTTP e o corpo da resposta.
///
/// O formato de saida e ProblemDetails (RFC 7807), que e o padrao do
/// ASP.NET Core para erros. O frontend le sempre a mesma estrutura, e o campo
/// "codigo" permite tratar cada caso sem depender do texto da mensagem.
///
/// Excecao nao prevista vira 500 com mensagem generica: detalhe interno e
/// registrado no log, nunca devolvido ao cliente.
/// </summary>
public class ManipuladorGlobalDeExcecoes : IExceptionHandler
{
    private readonly ILogger<ManipuladorGlobalDeExcecoes> _logger;

    public ManipuladorGlobalDeExcecoes(ILogger<ManipuladorGlobalDeExcecoes> logger) =>
        _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext contexto, Exception excecao, CancellationToken ct)
    {
        var problema = Traduzir(excecao, contexto);

        if (problema.Status >= 500)
            _logger.LogError(excecao, "Falha nao tratada em {Caminho}", contexto.Request.Path);
        else
            _logger.LogWarning("Regra violada em {Caminho}: {Mensagem}",
                contexto.Request.Path, excecao.Message);

        contexto.Response.StatusCode = problema.Status!.Value;
        await contexto.Response.WriteAsJsonAsync(problema, ct);

        return true;
    }

    private static ProblemDetails Traduzir(Exception excecao, HttpContext contexto)
    {
        var problema = excecao switch
        {
            SaldoInsuficienteExcecao e => Montar(
                StatusCodes.Status422UnprocessableEntity, "Saldo insuficiente", e,
                new Dictionary<string, object?>
                {
                    ["produtoCodigo"] = e.CodigoProduto,
                    ["saldoDisponivel"] = e.SaldoDisponivel,
                    ["quantidadeSolicitada"] = e.QuantidadeSolicitada
                }),

            CodigoDuplicadoExcecao e =>
                Montar(StatusCodes.Status409Conflict, "Codigo ja cadastrado", e),

            ProdutoEmUsoExcecao e =>
                Montar(StatusCodes.Status409Conflict, "Produto em uso", e),

            ProdutoNaoEncontradoExcecao e =>
                Montar(StatusCodes.Status404NotFound, "Produto nao encontrado", e),

            QuantidadeInvalidaExcecao e =>
                Montar(StatusCodes.Status400BadRequest, "Quantidade invalida", e),

            DadosInvalidosExcecao e =>
                Montar(StatusCodes.Status400BadRequest, "Dados invalidos", e),

            // Cobre ConflitoDeConcorrenciaExcecao e qualquer excecao de dominio
            // futura que nao tenha tratamento especifico.
            ExcecaoDeDominio e when e.Codigo == "CONFLITO_CONCORRENCIA" =>
                Montar(StatusCodes.Status409Conflict, "Conflito de concorrencia", e),

            ExcecaoDeDominio e =>
                Montar(StatusCodes.Status400BadRequest, "Regra de negocio violada", e),

            // 499 (client closed request) nao existe como constante no
            // ASP.NET Core, e a convencao do nginx para "o cliente desistiu".
            OperationCanceledException => new ProblemDetails
            {
                Status = 499,
                Title = "Requisicao cancelada"
            },

            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Erro interno",
                Detail = "Ocorreu uma falha inesperada ao processar a requisicao."
            }
        };

        // Permite correlacionar a resposta recebida pelo usuario com a linha
        // exata no log dos dois servicos.
        problema.Extensions["traceId"] =
            System.Diagnostics.Activity.Current?.Id ?? contexto.TraceIdentifier;

        return problema;
    }

    private static ProblemDetails Montar(
        int status, string titulo, ExcecaoDeDominio excecao,
        IDictionary<string, object?>? extras = null)
    {
        var problema = new ProblemDetails
        {
            Status = status,
            Title = titulo,
            Detail = excecao.Message
        };

        problema.Extensions["codigo"] = excecao.Codigo;

        if (extras is not null)
            foreach (var (chave, valor) in extras)
                problema.Extensions[chave] = valor;

        return problema;
    }
}
