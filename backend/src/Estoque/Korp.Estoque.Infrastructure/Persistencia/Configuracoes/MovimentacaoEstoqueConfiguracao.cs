using Korp.Estoque.Domain.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Korp.Estoque.Infrastructure.Persistencia.Configuracoes;

public class MovimentacaoEstoqueConfiguracao : IEntityTypeConfiguration<MovimentacaoEstoque>
{
    public void Configure(EntityTypeBuilder<MovimentacaoEstoque> builder)
    {
        builder.ToTable("movimentacoes_estoque");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");

        builder.Property(m => m.ProdutoId)
            .HasColumnName("produto_id")
            .IsRequired();

        // Referencia logica a uma nota fiscal que vive no banco do servico de
        // Faturamento. Nao existe chave estrangeira, e nem poderia existir:
        // sao bancos fisicamente separados. Indexado porque a consulta
        // "o que esta nota movimentou" e o caminho natural da auditoria.
        builder.Property(m => m.NotaId)
            .HasColumnName("nota_id")
            .IsRequired();

        builder.HasIndex(m => m.NotaId)
            .HasDatabaseName("ix_movimentacoes_nota_id");

        // Guardado como texto (BAIXA / ESTORNO) em vez de numero. Custa alguns
        // bytes e torna a tabela legivel para quem abrir o banco no vídeo.
        builder.Property(m => m.Tipo)
            .HasColumnName("tipo")
            .HasMaxLength(10)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(m => m.Quantidade)
            .HasColumnName("quantidade")
            .IsRequired();

        builder.Property(m => m.SaldoAnterior)
            .HasColumnName("saldo_anterior")
            .IsRequired();

        builder.Property(m => m.SaldoPosterior)
            .HasColumnName("saldo_posterior")
            .IsRequired();

        builder.Property(m => m.OcorridoEm)
            .HasColumnName("ocorrido_em")
            .HasColumnType("timestamptz")
            .IsRequired();
    }
}
