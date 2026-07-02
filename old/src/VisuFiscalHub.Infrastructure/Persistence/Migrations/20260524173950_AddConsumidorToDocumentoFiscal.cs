using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VisuFiscalHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConsumidorToDocumentoFiscal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CpfConsumidor",
                table: "documentos_fiscais",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NomeConsumidor",
                table: "documentos_fiscais",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CpfConsumidor",
                table: "documentos_fiscais");

            migrationBuilder.DropColumn(
                name: "NomeConsumidor",
                table: "documentos_fiscais");
        }
    }
}
