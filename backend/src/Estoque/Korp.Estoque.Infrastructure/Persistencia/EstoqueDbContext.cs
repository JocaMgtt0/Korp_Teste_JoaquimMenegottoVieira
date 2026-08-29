using Korp.Estoque.Domain.Entidades;
using Microsoft.EntityFrameworkCore;

namespace Korp.Estoque.Infrastructure.Persistencia;

/// <summary>
/// Contexto do banco do servico de Estoque.
///
/// Este contexto conhece apenas produtos e movimentacoes. Nao existe DbSet de
/// nota fiscal aqui, e isso e proposital: notas vivem em outro banco, de outro
/// servico. A ausencia e a garantia de que nao existe JOIN possivel entre os
/// dois dominios.
/// </summary>
public class EstoqueDbContext : DbContext
{
    public EstoqueDbContext(DbContextOptions<EstoqueDbContext> options) : base(options) { }

    public DbSet<Produto> Produtos => Set<Produto>();
    public DbSet<MovimentacaoEstoque> Movimentacoes => Set<MovimentacaoEstoque>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EstoqueDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
