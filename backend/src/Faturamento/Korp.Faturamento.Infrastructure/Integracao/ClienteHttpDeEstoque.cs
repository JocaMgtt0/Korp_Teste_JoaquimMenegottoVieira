using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Korp.Faturamento.Application.Contratos;
using Korp.Faturamento.Application.Dtos;
using Korp.Faturamento.Application.Excecoes;
using Korp.Faturamento.Domain.Excecoes;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Korp.Faturamento.Infrastructure.Integracao;

/// <summary>
/// Cliente HTTP do servico de Estoque.
///
/// Responsabilidade central: **traduzir a fronteira de rede em vocabulario de
/// dominio**. Acima desta classe ninguem sabe o que e um HttpResponseMessage,
/// um 422 ou um circuito aberto. O caso de uso de impressao so enxerga
/// EstoqueIndisponivelExcecao, SaldoInsuficienteNoEstoqueExcecao e
/// ConflitoDeConcorrenciaExcecao, e decide o que fazer com base nisso.
///
/// A distincao que mais importa e entre:
///
///   "o Estoque respondeu e recusou"  -> erro de negocio, nao adianta repetir
///   "o Estoque nao respondeu"        -> erro tecnico, a nota volta para Aberta
///
/// As politicas de retry, timeout e circuit breaker sao aplicadas por fora,
/// no registro do HttpClient, em InjecaoDeDependencia.
/// </summary>
public class ClienteHttpDeEstoque : IServicoDeEstoque
{
    private readonly HttpClient _http;
    private readonly ILogger<ClienteHttpDeEstoque> _logger;

    public ClienteHttpDeEstoque(HttpClient http, ILogger<ClienteHttpDeEstoque> logger)
    {
        _http = http;
        _logger = logger;
    }

    public Task BaixarAsync(
        Guid notaId, IReadOnlyList<ItemMovimentacao> itens, CancellationToken ct = default) =>
        EnviarMovimentacaoAsync("api/produtos/baixa", notaId, itens, ct);

    public Task EstornarAsync(
        Guid notaId, IReadOnlyList<ItemMovimentacao> itens, CancellationToken ct = default) =>
        EnviarMovimentacaoAsync("api/produtos/estorno", notaId, itens, ct);

    public async Task<IReadOnlyList<SaldoProdutoDto>> ConsultarSaldoAsync(
        IReadOnlyList<Guid> produtoIds, CancellationToken ct = default)
    {
        var resposta = await ExecutarAsync(
            () => _http.PostAsJsonAsync("api/produtos/consultar-saldo", produtoIds, ct));

        await GarantirSucessoAsync(resposta, ct);

        return await resposta.Content
            .ReadFromJsonAsync<List<SaldoProdutoDto>>(cancellationToken: ct)
            ?? new List<SaldoProdutoDto>();
    }

    private async Task EnviarMovimentacaoAsync(
        string rota, Guid notaId, IReadOnlyList<ItemMovimentacao> itens, CancellationToken ct)
    {
        var corpo = new
        {
            notaId,
            itens = itens.Select(i => new { produtoId = i.ProdutoId, quantidade = i.Quantidade })
        };

        var resposta = await ExecutarAsync(() => _http.PostAsJsonAsync(rota, corpo, ct));
        await GarantirSucessoAsync(resposta, ct);
    }

    /// <summary>
    /// Executa a chamada convertendo toda falha de transporte em
    /// <see cref="EstoqueIndisponivelExcecao"/>.
    ///
    /// Os tres casos cobertos:
    ///
    /// BrokenCircuitException  o circuito ja esta aberto por falhas anteriores,
    ///                         e a chamada nem chega a sair. E o comportamento
    ///                         desejado: nao adianta insistir em um servico
    ///                         que acabou de falhar cinco vezes.
    ///
    /// TimeoutRejectedException  o Estoque nao respondeu dentro do prazo.
    ///
    /// HttpRequestException      conexao recusada, DNS, servico fora do ar.
    ///                           E o que acontece com "docker compose stop estoque".
    /// </summary>
    private async Task<HttpResponseMessage> ExecutarAsync(Func<Task<HttpResponseMessage>> chamada)
    {
        try
        {
            return await chamada();
        }
        catch (BrokenCircuitException excecao)
        {
            _logger.LogWarning(
                "Circuito aberto para o servico de Estoque. Falhando rapido sem tentar a chamada.");

            throw new EstoqueIndisponivelExcecao(excecao);
        }
        catch (TimeoutRejectedException excecao)
        {
            _logger.LogWarning("Tempo esgotado ao chamar o servico de Estoque.");
            throw new EstoqueIndisponivelExcecao(excecao);
        }
        catch (HttpRequestException excecao)
        {
            _logger.LogWarning(excecao, "Falha de comunicacao com o servico de Estoque.");
            throw new EstoqueIndisponivelExcecao(excecao);
        }
        catch (TaskCanceledException excecao)
        {
            // TaskCanceledException tambem chega aqui quando o HttpClient
            // estoura o proprio tempo limite, e nao apenas quando o usuario
            // cancela a requisicao.
            _logger.LogWarning(excecao, "Chamada ao servico de Estoque cancelada ou expirada.");
            throw new EstoqueIndisponivelExcecao(excecao);
        }
    }

    /// <summary>
    /// Traduz o codigo de status em excecao de dominio.
    ///
    /// 5xx entra em <see cref="EstoqueIndisponivelExcecao"/> junto com as falhas
    /// de transporte: do ponto de vista do Faturamento, "o Estoque respondeu 500"
    /// e "o Estoque nao respondeu" tem o mesmo desfecho, a nota volta para Aberta.
    /// </summary>
    private async Task GarantirSucessoAsync(HttpResponseMessage resposta, CancellationToken ct)
    {
        if (resposta.IsSuccessStatusCode)
            return;

        var corpo = await resposta.Content.ReadAsStringAsync(ct);

        switch (resposta.StatusCode)
        {
            case HttpStatusCode.UnprocessableEntity:
                throw MontarSaldoInsuficiente(corpo);

            case HttpStatusCode.Conflict:
                throw new ConflitoDeConcorrenciaExcecao();

            case HttpStatusCode.NotFound:
                throw new DadosInvalidosExcecao(
                    "Um dos produtos da nota nao existe mais no servico de Estoque.");

            case HttpStatusCode.BadRequest:
                throw new DadosInvalidosExcecao(
                    ExtrairDetalhe(corpo) ?? "Requisicao recusada pelo servico de Estoque.");

            default:
                _logger.LogWarning(
                    "Servico de Estoque respondeu {Status}. Corpo: {Corpo}",
                    (int)resposta.StatusCode, corpo);

                throw new EstoqueIndisponivelExcecao();
        }
    }

    private static SaldoInsuficienteNoEstoqueExcecao MontarSaldoInsuficiente(string corpo)
    {
        try
        {
            using var documento = JsonDocument.Parse(corpo);
            var raiz = documento.RootElement;

            var codigo = raiz.TryGetProperty("produtoCodigo", out var c) ? c.GetString() : null;
            var disponivel = raiz.TryGetProperty("saldoDisponivel", out var d) ? d.GetInt32() : 0;
            var solicitado = raiz.TryGetProperty("quantidadeSolicitada", out var s) ? s.GetInt32() : 0;
            var detalhe = raiz.TryGetProperty("detail", out var t) ? t.GetString() : null;

            var faltas = codigo is null
                ? Array.Empty<FaltaDeSaldo>()
                : new[] { new FaltaDeSaldo(codigo, disponivel, solicitado) };

            return new SaldoInsuficienteNoEstoqueExcecao(faltas, detalhe);
        }
        catch (JsonException)
        {
            // Corpo em formato inesperado nao pode derrubar o fluxo: o que
            // importa e que a baixa foi recusada por falta de saldo.
            return new SaldoInsuficienteNoEstoqueExcecao(Array.Empty<FaltaDeSaldo>());
        }
    }

    private static string? ExtrairDetalhe(string corpo)
    {
        try
        {
            using var documento = JsonDocument.Parse(corpo);
            return documento.RootElement.TryGetProperty("detail", out var d) ? d.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
