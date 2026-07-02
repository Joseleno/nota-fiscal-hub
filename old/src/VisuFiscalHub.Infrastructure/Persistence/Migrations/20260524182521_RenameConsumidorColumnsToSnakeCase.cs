using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VisuFiscalHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameConsumidorColumnsToSnakeCase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "NomeConsumidor",
                table: "documentos_fiscais",
                newName: "nome_consumidor");

            migrationBuilder.RenameColumn(
                name: "CpfConsumidor",
                table: "documentos_fiscais",
                newName: "cpf_consumidor");

            migrationBuilder.AlterColumn<string>(
                name: "nome_consumidor",
                table: "documentos_fiscais",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "cpf_consumidor",
                table: "documentos_fiscais",
                type: "character varying(11)",
                maxLength: 11,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "nome_consumidor",
                table: "documentos_fiscais",
                newName: "NomeConsumidor");

            migrationBuilder.RenameColumn(
                name: "cpf_consumidor",
                table: "documentos_fiscais",
                newName: "CpfConsumidor");

            migrationBuilder.AlterColumn<string>(
                name: "NomeConsumidor",
                table: "documentos_fiscais",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(60)",
                oldMaxLength: 60,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CpfConsumidor",
                table: "documentos_fiscais",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(11)",
                oldMaxLength: 11,
                oldNullable: true);
        }
    }
}
