using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;
using Xunit;

namespace Korp.Estoque.Tests.Integracao;

/// <summary>
/// Sobe a API de Estoque inteira contra um PostgreSQL real em container.
///
/// Diferente dos testes de dominio, aqui nada e simulado: as migrations sao
/// aplicadas de verdade, as consultas viram SQL de verdade e o controle de
/// concorrencia otimista e exercitado pelo banco de verdade.
///
/// E o unico jeito honesto de testar concorrencia. Com duble, o teste provaria
/// apenas que o codigo chama os metodos certos, nao que duas transacoes
/// simultaneas disputando a mesma linha terminam de forma consistente.
///
/// O container e descartavel: sobe antes dos testes, morre depois, e nao
/// deixa nada na maquina nem depende de banco previamente instalado.
/// </summary>
public class FabricaDeApiDeEstoque : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _banco = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("estoque_teste")
        .WithUsername("korp")
        .WithPassword("korp")
        .Build();

    public async Task InitializeAsync() => await _banco.StartAsync();

    /// <summary>
    /// Implementacao explicita porque WebApplicationFactory ja possui um
    /// DisposeAsync proprio, com assinatura diferente da exigida pelo xUnit.
    /// </summary>
    async Task IAsyncLifetime.DisposeAsync()
    {
        await _banco.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Aponta a aplicacao para o banco do container em vez do configurado
        // em appsettings. O Program continua rodando exatamente como em
        // producao, inclusive aplicando migrations e semeando o catalogo.
        builder.UseSetting("ConnectionStrings:Padrao", _banco.GetConnectionString());
        builder.UseEnvironment("Development");
    }
}
