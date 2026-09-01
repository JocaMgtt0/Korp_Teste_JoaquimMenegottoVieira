using System.Net;
using System.Text;
using FluentAssertions;
using Korp.Faturamento.Application.Dtos;
using Korp.Faturamento.Application.Excecoes;
using Korp.Faturamento.Domain.Excecoes;
using Korp.Faturamento.Infrastructure.Integracao;
using Microsoft.Extensions.Logging.Abstractions;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Xunit;

namespace Korp.Faturamento.Tests.Integracao;

/// <summary>
/// Handler HTTP falso: responde o que o teste mandar, ou lanca o que o teste
/// mandar. Evita subir servidor so para verificar traducao de erro.
/// </summary>
public class HandlerFalso : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public HandlerFalso(HttpStatusCode status, string corpo = "")
        => _responder = _ => new HttpResponseMessage(status)
        {
            Content = new StringContent(corpo, Encoding.UTF8, "application/json")
        };

    public HandlerFalso(Exception falha)
        => _responder = _ => throw falha;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage requisicao, CancellationToken ct) =>
        Task.FromResult(_responder(requisicao));
}

/// <summary>
/// Testes do cliente do servico de Estoque.
///
/// Esta classe tem uma responsabilidade unica e critica: **traduzir a
/// fronteira de rede em vocabulario de dominio**. Acima dela ninguem sabe o
/// que e um 422 ou um circuito aberto.
///
/// A traducao precisa de teste proprio porque e ela que decide o
/// comportamento da saga de impressao. Confundir "o Estoque recusou" com
/// "o Estoque nao respondeu" faria o sistema compensar quando nao deveria,
/// ou deixar de compensar quando deveria.
/// </summary>
public class ClienteHttpDeEstoqueTestes
{
    private static ClienteHttpDeEstoque Cliente(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://estoque-falso/") },
            NullLogger<ClienteHttpDeEstoque>.Instance);

    private static readonly IReadOnlyList<ItemMovimentacao> Itens =
        new[] { new ItemMovimentacao(Guid.NewGuid(), 2) };

    // ---------- Sucesso ----------

    [Fact]
    public async Task Baixa_bem_sucedida_nao_lanca()
    {
        var cliente = Cliente(new HandlerFalso(HttpStatusCode.NoContent));

        var acao = async () => await cliente.BaixarAsync(Guid.NewGuid(), Itens);

        await acao.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ConsultarSaldo_desserializa_a_resposta()
    {
        var id = Guid.NewGuid();
        var corpo = $$"""
            [{"id":"{{id}}","codigo":"PRD-001","descricao":"Teclado","saldo":7}]
            """;

        var cliente = Cliente(new HandlerFalso(HttpStatusCode.OK, corpo));

        var saldos = await cliente.ConsultarSaldoAsync(new[] { id });

        saldos.Should().ContainSingle();
        saldos[0].Codigo.Should().Be("PRD-001");
        saldos[0].Saldo.Should().Be(7);
    }

    // ---------- Recusa de negocio ----------

    [Fact]
    public async Task Resposta_422_vira_saldo_insuficiente_com_o_detalhamento()
    {
        var corpo = """
            {"title":"Saldo insuficiente",
             "detail":"O produto 'PRD-001' possui saldo 1 e a operacao requer 3 unidades.",
             "codigo":"SALDO_INSUFICIENTE",
             "produtoCodigo":"PRD-001","saldoDisponivel":1,"quantidadeSolicitada":3}
            """;

        var cliente = Cliente(new HandlerFalso(HttpStatusCode.UnprocessableEntity, corpo));

        var acao = async () => await cliente.BaixarAsync(Guid.NewGuid(), Itens);

        var excecao = (await acao.Should().ThrowAsync<SaldoInsuficienteNoEstoqueExcecao>()).Which;

        excecao.Faltas.Should().ContainSingle();
        excecao.Faltas[0].ProdutoCodigo.Should().Be("PRD-001");
        excecao.Faltas[0].SaldoDisponivel.Should().Be(1);
        excecao.Faltas[0].QuantidadeSolicitada.Should().Be(3);
    }

    [Fact]
    public async Task Resposta_422_com_corpo_ilegivel_ainda_vira_saldo_insuficiente()
    {
        var cliente = Cliente(new HandlerFalso(HttpStatusCode.UnprocessableEntity, "isto nao e json"));

        var acao = async () => await cliente.BaixarAsync(Guid.NewGuid(), Itens);

        // Corpo em formato inesperado nao pode derrubar o fluxo: o que importa
        // e que a baixa foi recusada por falta de saldo.
        var excecao = (await acao.Should().ThrowAsync<SaldoInsuficienteNoEstoqueExcecao>()).Which;
        excecao.Faltas.Should().BeEmpty();
    }

    [Fact]
    public async Task Resposta_409_vira_conflito_de_concorrencia()
    {
        var cliente = Cliente(new HandlerFalso(HttpStatusCode.Conflict));

        var acao = async () => await cliente.BaixarAsync(Guid.NewGuid(), Itens);

        await acao.Should().ThrowAsync<ConflitoDeConcorrenciaExcecao>();
    }

    [Fact]
    public async Task Resposta_404_vira_dados_invalidos()
    {
        var cliente = Cliente(new HandlerFalso(HttpStatusCode.NotFound));

        var acao = async () => await cliente.BaixarAsync(Guid.NewGuid(), Itens);

        await acao.Should().ThrowAsync<DadosInvalidosExcecao>();
    }

    // ---------- Falha tecnica ----------

    [Fact]
    public async Task Servico_fora_do_ar_vira_estoque_indisponivel()
    {
        var cliente = Cliente(new HandlerFalso(new HttpRequestException("connection refused")));

        var acao = async () => await cliente.BaixarAsync(Guid.NewGuid(), Itens);

        await acao.Should().ThrowAsync<EstoqueIndisponivelExcecao>();
    }

    [Fact]
    public async Task Timeout_vira_estoque_indisponivel()
    {
        var cliente = Cliente(new HandlerFalso(new TimeoutRejectedException()));

        var acao = async () => await cliente.BaixarAsync(Guid.NewGuid(), Itens);

        await acao.Should().ThrowAsync<EstoqueIndisponivelExcecao>();
    }

    [Fact]
    public async Task Circuito_aberto_vira_estoque_indisponivel()
    {
        var cliente = Cliente(new HandlerFalso(new BrokenCircuitException("circuito aberto")));

        var acao = async () => await cliente.BaixarAsync(Guid.NewGuid(), Itens);

        // Falha rapida: o circuito aberto nem deixa a chamada sair, e mesmo
        // assim o resto do sistema recebe o mesmo vocabulario de sempre.
        await acao.Should().ThrowAsync<EstoqueIndisponivelExcecao>();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Erro_5xx_vira_estoque_indisponivel(HttpStatusCode status)
    {
        var cliente = Cliente(new HandlerFalso(status));

        var acao = async () => await cliente.BaixarAsync(Guid.NewGuid(), Itens);

        // Do ponto de vista do Faturamento, "o Estoque respondeu 500" e
        // "o Estoque nao respondeu" tem o mesmo desfecho: a nota volta
        // para Aberta e nenhum saldo foi alterado.
        await acao.Should().ThrowAsync<EstoqueIndisponivelExcecao>();
    }

    // ---------- Estorno ----------

    [Fact]
    public async Task Estorno_bem_sucedido_nao_lanca()
    {
        var cliente = Cliente(new HandlerFalso(HttpStatusCode.NoContent));

        var acao = async () => await cliente.EstornarAsync(Guid.NewGuid(), Itens);

        await acao.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Estorno_com_servico_fora_do_ar_propaga_indisponibilidade()
    {
        var cliente = Cliente(new HandlerFalso(new HttpRequestException("connection refused")));

        var acao = async () => await cliente.EstornarAsync(Guid.NewGuid(), Itens);

        // E esta excecao que leva a saga ao caso INTERVENCAO_MANUAL:
        // a baixa saiu, a compensacao falhou, e a nota fica sinalizada.
        await acao.Should().ThrowAsync<EstoqueIndisponivelExcecao>();
    }
}
