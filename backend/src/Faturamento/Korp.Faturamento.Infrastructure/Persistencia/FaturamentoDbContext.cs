using Korp.Faturamento.Domain.Entidades;
using Microsoft.EntityFrameworkCore;

namespace Korp.Faturamento.Infrastructure.Persistencia;

/// <summary>
/// Contexto do banco do servico de Faturamento.
///
/// Nao existe DbSet de Produto aqui. Os dados de produto que a nota precisa
/// (codigo e descricao) vivem como copia dentro do proprio item, gravados no
/// momento da inclusao. E o que permite imprimir uma nota com o servico de
/// Estoque fora do ar.
/// </summary>
public class FaturamentoDbContext : DbContext
{
    /// <summary>
    /// Sequence que gera a numeracao das notas (RN04).
    ///
    /// Sequence e nao MAX(numero) + 1: sequence e atomica no PostgreSQL e nunca
    /// entrega o mesmo valor duas vezes, mesmo com dez requisicoes simultaneas.
    /// Com MAX, duas criacoes concorrentes leriam o mesmo maximo e tentariam
    /// gravar o mesmo numero.
    /// </summary>
    public const string SequenceNumeroNota = "seq_numero_nota";

    public FaturamentoDbContext(DbContextOptions<FaturamentoDbContext> options) : base(options) { }

    public DbSet<NotaFiscal> Notas => Set<NotaFiscal>();
    public DbSet<ItemNotaFiscal> Itens => Set<ItemNotaFiscal>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>(SequenceNumeroNota)
            .StartsAt(1)
            .IncrementsBy(1);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FaturamentoDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
