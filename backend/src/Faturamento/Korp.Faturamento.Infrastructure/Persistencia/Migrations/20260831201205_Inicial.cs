using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Korp.Faturamento.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "seq_numero_nota");

            migrationBuilder.CreateTable(
                name: "notas_fiscais",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    criada_em = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    fechada_em = table.Column<DateTime>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notas_fiscais", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "itens_nota_fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nota_fiscal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    produto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    produto_codigo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    produto_descricao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    quantidade = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_itens_nota_fiscal", x => x.id);
                    table.CheckConstraint("ck_itens_quantidade_positiva", "quantidade > 0");
                    table.ForeignKey(
                        name: "FK_itens_nota_fiscal_notas_fiscais_nota_fiscal_id",
                        column: x => x.nota_fiscal_id,
                        principalTable: "notas_fiscais",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_itens_nota_fiscal_nota_fiscal_id",
                table: "itens_nota_fiscal",
                column: "nota_fiscal_id");

            migrationBuilder.CreateIndex(
                name: "ix_notas_fiscais_numero",
                table: "notas_fiscais",
                column: "numero",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notas_fiscais_status",
                table: "notas_fiscais",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "itens_nota_fiscal");

            migrationBuilder.DropTable(
                name: "notas_fiscais");

            migrationBuilder.DropSequence(
                name: "seq_numero_nota");
        }
    }
}
