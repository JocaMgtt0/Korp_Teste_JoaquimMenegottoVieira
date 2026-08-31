using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Korp.Estoque.Application.Dtos;
using Xunit;

namespace Korp.Estoque.Tests.Integracao;

/// <summary>
/// Testes de ponta a ponta do servico de Estoque: HTTP de entrada,
/// PostgreSQL real de saida.
/// </summary>
public class EstoqueIntegracaoTestes : IClassFixture<FabricaDeApiDeEstoque>
{
    private readonly HttpClient _cliente;

    public EstoqueIntegracaoTestes(FabricaDeApiDeEstoque fabrica) =>
        _cliente = fabrica.CreateClient();

    /// <summary>Cada teste cria o proprio produto, para nao depender de ordem de execucao.</summary>
    private async Task<ProdutoDto> CriarProdutoAsync(int saldo, string? codigo = null)
    {
        var dto = new CriarProdutoDto(
            codigo ?? $"TST-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            "Produto de teste",
            saldo);

        var resposta = await _cliente.PostAsJsonAsync("/api/produtos", dto);
        resposta.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await resposta.Content.ReadFromJsonAsync<ProdutoDto>())!;
    }

    private async Task<int> SaldoAtualAsync(Guid id)
    {
        var produto = await _cliente.GetFromJsonAsync<ProdutoDto>($"/api/produtos/{id}");
        return produto!.Saldo;
    }

    [Fact]
    public async Task Aplicacao_sobe_e_responde_o_health_check()
    {
        var resposta = await _cliente.GetAsync("/health");

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Catalogo_e_semeado_na_primeira_subida()
    {
        var resultado = await _cliente
            .GetFromJsonAsync<ResultadoPaginado<ProdutoDto>>("/api/produtos?tamanho=100");

        // A semeadura roda dentro do Program, entao este teste tambem prova
        // que migrations e seed funcionam contra um banco limpo.
        resultado!.Itens.Should().Contain(p => p.Codigo == "PRD-001");
        resultado.Itens.Should().Contain(p => p.Codigo == "PRD-009" && p.Saldo == 1);
    }

    [Fact]
    public async Task Codigo_duplicado_e_recusado_pelo_indice_unico()
    {
        var codigo = $"DUP-{Guid.NewGuid().ToString("N")[..8]}";
        await CriarProdutoAsync(5, codigo);

        var resposta = await _cliente.PostAsJsonAsync(
            "/api/produtos", new CriarProdutoDto(codigo, "Outro produto", 3));

        resposta.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Busca_encontra_por_codigo_e_por_descricao_sem_diferenciar_maiusculas()
    {
        await CriarProdutoAsync(5, "BUSCA-ABC");

        var porCodigo = await _cliente
            .GetFromJsonAsync<ResultadoPaginado<ProdutoDto>>("/api/produtos?busca=busca-abc");

        // Prova que o ILIKE do PostgreSQL esta sendo usado: a busca em
        // minusculas encontra o codigo cadastrado em maiusculas.
        porCodigo!.Itens.Should().Contain(p => p.Codigo == "BUSCA-ABC");
    }

    [Fact]
    public async Task Baixa_reduz_o_saldo_no_banco()
    {
        var produto = await CriarProdutoAsync(10);

        var resposta = await _cliente.PostAsJsonAsync("/api/produtos/baixa",
            new MovimentacaoEstoqueDto(Guid.NewGuid(),
                new[] { new ItemMovimentacaoDto(produto.Id, 2) }));

        resposta.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Exemplo literal do enunciado: saldo 10, nota usa 2, novo saldo 8.
        (await SaldoAtualAsync(produto.Id)).Should().Be(8);
    }

    [Fact]
    public async Task Baixa_sem_saldo_suficiente_e_recusada_com_detalhamento()
    {
        var produto = await CriarProdutoAsync(3);

        var resposta = await _cliente.PostAsJsonAsync("/api/produtos/baixa",
            new MovimentacaoEstoqueDto(Guid.NewGuid(),
                new[] { new ItemMovimentacaoDto(produto.Id, 5) }));

        resposta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var corpo = await resposta.Content.ReadAsStringAsync();
        corpo.Should().Contain("SALDO_INSUFICIENTE");
        corpo.Should().Contain("saldoDisponivel");

        (await SaldoAtualAsync(produto.Id)).Should().Be(3);
    }

    [Fact]
    public async Task Baixa_com_varios_itens_e_atomica_quando_um_deles_falha()
    {
        var comSaldo = await CriarProdutoAsync(10);
        var semSaldo = await CriarProdutoAsync(1);

        var resposta = await _cliente.PostAsJsonAsync("/api/produtos/baixa",
            new MovimentacaoEstoqueDto(Guid.NewGuid(), new[]
            {
                new ItemMovimentacaoDto(comSaldo.Id, 2),
                new ItemMovimentacaoDto(semSaldo.Id, 5)
            }));

        resposta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // RN10: o primeiro item tinha saldo de sobra, mas a transacao inteira
        // foi desfeita por causa do segundo. Nenhuma baixa parcial sobrevive.
        (await SaldoAtualAsync(comSaldo.Id)).Should().Be(10);
        (await SaldoAtualAsync(semSaldo.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Estorno_devolve_o_saldo_baixado()
    {
        var produto = await CriarProdutoAsync(10);
        var notaId = Guid.NewGuid();

        var itens = new[] { new ItemMovimentacaoDto(produto.Id, 4) };

        await _cliente.PostAsJsonAsync("/api/produtos/baixa",
            new MovimentacaoEstoqueDto(notaId, itens));

        (await SaldoAtualAsync(produto.Id)).Should().Be(6);

        await _cliente.PostAsJsonAsync("/api/produtos/estorno",
            new MovimentacaoEstoqueDto(notaId, itens));

        (await SaldoAtualAsync(produto.Id)).Should().Be(10);
    }

    [Fact]
    public async Task Produto_ja_movimentado_nao_pode_ser_excluido()
    {
        var produto = await CriarProdutoAsync(5);

        await _cliente.PostAsJsonAsync("/api/produtos/baixa",
            new MovimentacaoEstoqueDto(Guid.NewGuid(),
                new[] { new ItemMovimentacaoDto(produto.Id, 1) }));

        var resposta = await _cliente.DeleteAsync($"/api/produtos/{produto.Id}");

        // RN09
        resposta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await resposta.Content.ReadAsStringAsync()).Should().Contain("PRODUTO_EM_USO");
    }

    [Fact]
    public async Task Produto_nunca_movimentado_pode_ser_excluido()
    {
        var produto = await CriarProdutoAsync(5);

        var resposta = await _cliente.DeleteAsync($"/api/produtos/{produto.Id}");

        resposta.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _cliente.GetAsync($"/api/produtos/{produto.Id}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }
}
