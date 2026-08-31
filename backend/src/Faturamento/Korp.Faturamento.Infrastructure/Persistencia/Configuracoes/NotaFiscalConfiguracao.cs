using Korp.Faturamento.Domain.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Korp.Faturamento.Infrastructure.Persistencia.Configuracoes;

public class NotaFiscalConfiguracao : IEntityTypeConfiguration<NotaFiscal>
{
    public void Configure(EntityTypeBuilder<NotaFiscal> builder)
    {
        builder.ToTable("notas_fiscais");

        builder.HasKey(n => n.Id);

        // As entidades geram o proprio Guid no construtor. Sem
        // ValueGeneratedNever, o EF assume chave gerada por ele e usa a
        // heuristica "chave preenchida significa registro existente",
        // emitindo UPDATE onde deveria ser INSERT.
        builder.Property(n => n.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(n => n.Numero)
            .HasColumnName("numero")
            .IsRequired();

        // RN04: numeracao unica. O indice e a garantia final, mesmo que algo
        // contorne a sequence.
        builder.HasIndex(n => n.Numero)
            .IsUnique()
            .HasDatabaseName("ix_notas_fiscais_numero");

        // Status como texto (Aberta / EmProcessamento / Fechada) em vez de
        // numero: a tabela fica legivel para quem abrir o banco durante a
        // demonstracao, e o custo e irrelevante.
        builder.Property(n => n.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .HasConversion<string>()
            .IsRequired();

        builder.HasIndex(n => n.Status)
            .HasDatabaseName("ix_notas_fiscais_status");

        builder.Property(n => n.CriadaEm)
            .HasColumnName("criada_em")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(n => n.FechadaEm)
            .HasColumnName("fechada_em")
            .HasColumnType("timestamptz");

        builder.HasMany(n => n.Itens)
            .WithOne()
            .HasForeignKey(i => i.NotaFiscalId)
            .OnDelete(DeleteBehavior.Cascade);

        // A colecao e exposta como somente leitura e alimentada por um campo
        // privado, entao o EF precisa escrever no campo, nao na propriedade.
        builder.Metadata
            .FindNavigation(nameof(NotaFiscal.Itens))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class ItemNotaFiscalConfiguracao : IEntityTypeConfiguration<ItemNotaFiscal>
{
    public void Configure(EntityTypeBuilder<ItemNotaFiscal> builder)
    {
        builder.ToTable("itens_nota_fiscal", t =>
            t.HasCheckConstraint("ck_itens_quantidade_positiva", "quantidade > 0"));

        builder.HasKey(i => i.Id);

        // Este e o caso que revelou o problema: o item nasce dentro do
        // agregado NotaFiscal e chega ao EF pela colecao de navegacao,
        // exatamente o cenario em que a heuristica de chave decide errado.
        builder.Property(i => i.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(i => i.NotaFiscalId)
            .HasColumnName("nota_fiscal_id")
            .IsRequired();

        // Referencia logica ao produto, que vive no banco do servico de
        // Estoque. Sem chave estrangeira, por definicao de microsservico.
        builder.Property(i => i.ProdutoId)
            .HasColumnName("produto_id")
            .IsRequired();

        // RN11: copia do produto no momento da inclusao. Nao e desnormalizacao
        // por descuido: e o que desacopla os dois servicos na leitura e o que
        // faz a nota de ontem continuar mostrando o texto de ontem.
        builder.Property(i => i.ProdutoCodigo)
            .HasColumnName("produto_codigo")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(i => i.ProdutoDescricao)
            .HasColumnName("produto_descricao")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(i => i.Quantidade)
            .HasColumnName("quantidade")
            .IsRequired();
    }
}
