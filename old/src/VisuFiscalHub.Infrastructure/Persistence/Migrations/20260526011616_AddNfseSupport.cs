using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VisuFiscalHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNfseSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_documentos_fiscais_chave_acesso",
                table: "documentos_fiscais");

            migrationBuilder.AddColumn<string>(
                name: "inscricao_municipal",
                table: "tenants",
                type: "character varying(15)",
                maxLength: 15,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "chave_acesso",
                table: "documentos_fiscais",
                type: "character varying(44)",
                maxLength: 44,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(44)",
                oldMaxLength: 44);

            migrationBuilder.AddColumn<string>(
                name: "servico_nfse",
                table: "documentos_fiscais",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tomador",
                table: "documentos_fiscais",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_documentos_fiscais_chave_acesso",
                table: "documentos_fiscais",
                column: "chave_acesso",
                filter: "chave_acesso IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_documentos_fiscais_chave_acesso",
                table: "documentos_fiscais");

            migrationBuilder.DropColumn(
                name: "inscricao_municipal",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "servico_nfse",
                table: "documentos_fiscais");

            migrationBuilder.DropColumn(
                name: "tomador",
                table: "documentos_fiscais");

            migrationBuilder.AlterColumn<string>(
                name: "chave_acesso",
                table: "documentos_fiscais",
                type: "character varying(44)",
                maxLength: 44,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(44)",
                oldMaxLength: 44,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_documentos_fiscais_chave_acesso",
                table: "documentos_fiscais",
                column: "chave_acesso");
        }
    }
}
