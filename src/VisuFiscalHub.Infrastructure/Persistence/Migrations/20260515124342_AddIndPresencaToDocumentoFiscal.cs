using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VisuFiscalHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIndPresencaToDocumentoFiscal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ind_presenca",
                table: "documentos_fiscais",
                type: "integer",
                nullable: false,
                defaultValue: 1); // 1 = presencial — único default semanticamente válido para NFC-e
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ind_presenca",
                table: "documentos_fiscais");
        }
    }
}
