using Korp.Estoque.Application.Contratos;
using Korp.Estoque.Application.Servicos;
using Korp.Estoque.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Korp.Estoque.Infrastructure;

/// <summary>
/// Registro dos servicos de Infrastructure e Application no container.
///
/// Fica nesta camada para que o projeto de Api nao precise conhecer o Entity
/// Framework nem o nome das classes concretas. A Api chama um metodo e pronto.
/// </summary>
public static class InjecaoDeDependencia
{
    public static IServiceCollection AdicionarInfraestrutura(
        this IServiceCollection servicos, IConfiguration configuracao)
    {
        var conexao = configuracao.GetConnectionString("Padrao")
            ?? throw new InvalidOperationException(
                "Connection string 'Padrao' nao configurada.");

        servicos.AddDbContext<EstoqueDbContext>(opcoes =>
            opcoes.UseNpgsql(conexao));

        servicos.AddScoped<IProdutoRepositorio, ProdutoRepositorio>();
        servicos.AddScoped<IUnidadeDeTrabalho, UnidadeDeTrabalho>();

        servicos.AddScoped<ServicoDeProdutos>();
        servicos.AddScoped<ServicoDeMovimentacao>();

        return servicos;
    }

    /// <summary>
    /// Aplica as migrations pendentes e semeia o catalogo.
    ///
    /// Rodar migration no start e uma escolha para este contexto: o avaliador
    /// sobe o compose e tem o sistema pronto, sem passo manual. Em producao
    /// de verdade isso normalmente fica num job separado do deploy.
    /// </summary>
    public static async Task PrepararBancoAsync(this IServiceProvider provedor, CancellationToken ct = default)
    {
        using var escopo = provedor.CreateScope();

        var contexto = escopo.ServiceProvider.GetRequiredService<EstoqueDbContext>();
        var logger = escopo.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(nameof(PrepararBancoAsync));

        logger.LogInformation("Aplicando migrations do banco de Estoque.");
        await contexto.Database.MigrateAsync(ct);

        await SemeadorDeDados.SemearAsync(contexto, logger, ct);
    }
}
