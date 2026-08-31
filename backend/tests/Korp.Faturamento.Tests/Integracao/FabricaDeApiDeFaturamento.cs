using Korp.Faturamento.Application.Contratos;
using Korp.Faturamento.Application.Dtos;
using Korp.Faturamento.Domain.Entidades;
using Korp.Faturamento.Infrastructure.Pdf;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Xunit;

namespace Korp.Faturamento.Tests.Integracao;

/// <summary>
/// Duble controlavel do servico de Estoque.
///
/// Nos testes de integracao do Faturamento, o banco e real mas o vizinho e
/// simulado. Isso e proposital: o objetivo aqui e provar que a saga de
/// impressao percorre os estados corretos e persiste o resultado certo em
/// cada desfecho, e nao testar o servico de Estoque de novo, que ja tem os
/// proprios testes de integracao.
///
/// Com o duble, cenarios caros ou impossiveis de reproduzir de forma
/// confiavel viram uma linha: "o Estoque esta fora do ar", "o Estoque
/// recusou por falta de saldo", "o estorno tambem falhou".
/// </summary>
public class ServicoDeEstoqueControlavel : IServicoDeEstoque
{
    public Exception? FalhaNaBaixa { get; set; }
    public Exception? FalhaNoEstorno { get; set; }

    public List<(Guid NotaId, IReadOnlyList<ItemMovimentacao> Itens)> Baixas { get; } = new();
    public List<(Guid NotaId, IReadOnlyList<ItemMovimentacao> Itens)> Estornos { get; } = new();

    public List<SaldoProdutoDto> Catalogo { get; } = new();

    public void Reiniciar()
    {
        FalhaNaBaixa = null;
        FalhaNoEstorno = null;
        Baixas.Clear();
        Estornos.Clear();
    }

    public Task BaixarAsync(Guid notaId, IReadOnlyList<ItemMovimentacao> itens, CancellationToken ct = default)
    {
        if (FalhaNaBaixa is not null) throw FalhaNaBaixa;

        Baixas.Add((notaId, itens));
        return Task.CompletedTask;
    }

    public Task EstornarAsync(Guid notaId, IReadOnlyList<ItemMovimentacao> itens, CancellationToken ct = default)
    {
        if (FalhaNoEstorno is not null) throw FalhaNoEstorno;

        Estornos.Add((notaId, itens));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SaldoProdutoDto>> ConsultarSaldoAsync(
        IReadOnlyList<Guid> produtoIds, CancellationToken ct = default)
    {
        IReadOnlyList<SaldoProdutoDto> encontrados = Catalogo
            .Where(p => produtoIds.Contains(p.Id))
            .ToList();

        return Task.FromResult(encontrados);
    }
}

/// <summary>
/// Gerador de PDF que envolve o real e pode ser instruido a falhar.
/// Usado para exercitar o unico ponto da saga em que ha efeito a compensar.
/// </summary>
public class GeradorDePdfControlavel : IGeradorDePdf
{
    private readonly GeradorDePdfQuestPdf _real = new();

    public Exception? Falha { get; set; }

    public byte[] Gerar(NotaFiscal nota)
    {
        if (Falha is not null) throw Falha;

        return _real.Gerar(nota);
    }
}

public class FabricaDeApiDeFaturamento : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _banco = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("faturamento_teste")
        .WithUsername("korp")
        .WithPassword("korp")
        .Build();

    public ServicoDeEstoqueControlavel Estoque { get; } = new();
    public GeradorDePdfControlavel GeradorDePdf { get; } = new();

    public async Task InitializeAsync() => await _banco.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _banco.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Padrao", _banco.GetConnectionString());
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(servicos =>
        {
            // RemoveAll e necessario porque AddHttpClient registra a
            // implementacao real, e adicionar outra sem remover deixaria as
            // duas no container, com a ultima vencendo por acidente.
            servicos.RemoveAll<IServicoDeEstoque>();
            servicos.AddSingleton<IServicoDeEstoque>(Estoque);

            servicos.RemoveAll<IGeradorDePdf>();
            servicos.AddSingleton<IGeradorDePdf>(GeradorDePdf);
        });
    }
}
