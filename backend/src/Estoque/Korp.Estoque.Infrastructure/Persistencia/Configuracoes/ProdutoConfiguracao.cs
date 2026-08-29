using Korp.Estoque.Domain.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Korp.Estoque.Infrastructure.Persistencia.Configuracoes;

public class ProdutoConfiguracao : IEntityTypeConfiguration<Produto>
{
    public void Configure(EntityTypeBuilder<Produto> builder)
    {
        builder.ToTable("produtos", t =>
            // Ultima linha de defesa da RN02. As regras ja impedem saldo
            // negativo no dominio, mas uma constraint no banco protege
            // tambem contra escrita manual e contra bug futuro.
            t.HasCheckConstraint("ck_produtos_saldo_nao_negativo", "saldo >= 0"));

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");

        builder.Property(p => p.Codigo)
            .HasColumnName("codigo")
            .HasMaxLength(Produto.TamanhoMaximoCodigo)
            .IsRequired();

        // RN01. O indice unico e o que garante a regra sob concorrencia:
        // duas requisicoes simultaneas com o mesmo codigo passariam pela
        // verificacao da camada de aplicacao, mas so uma sobrevive ao insert.
        builder.HasIndex(p => p.Codigo)
            .IsUnique()
            .HasDatabaseName("ix_produtos_codigo");

        builder.Property(p => p.Descricao)
            .HasColumnName("descricao")
            .HasMaxLength(Produto.TamanhoMaximoDescricao)
            .IsRequired();

        builder.Property(p => p.Saldo)
            .HasColumnName("saldo")
            .IsRequired();

        // Concorrencia otimista. O EF inclui o valor original desta coluna no
        // WHERE de todo UPDATE. Se outra transacao alterou a linha no meio do
        // caminho, o UPDATE afeta zero linhas e o EF lanca
        // DbUpdateConcurrencyException, tratada em UnidadeDeTrabalho.
        builder.Property(p => p.Versao)
            .HasColumnName("versao")
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(p => p.CriadoEm)
            .HasColumnName("criado_em")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(p => p.AtualizadoEm)
            .HasColumnName("atualizado_em")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.HasMany(p => p.Movimentacoes)
            .WithOne()
            .HasForeignKey(m => m.ProdutoId)
            .OnDelete(DeleteBehavior.Cascade);

        // A colecao e exposta como somente leitura e alimentada por um campo
        // privado. Sem isto o EF tentaria escrever pela propriedade, que nao
        // tem setter, e falharia.
        builder.Metadata
            .FindNavigation(nameof(Produto.Movimentacoes))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
