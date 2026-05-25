using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VisuFiscalHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNfeSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "serie_nfe",
                table: "tenants",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "mod_frete",
                table: "documentos_fiscais",
                type: "integer",
                nullable: false,
                defaultValue: 9);

            migrationBuilder.AddColumn<string>(
                name: "nat_op",
                table: "documentos_fiscais",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "nfe_destinatario",
                table: "documentos_fiscais",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "serie_nfe",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "mod_frete",
                table: "documentos_fiscais");

            migrationBuilder.DropColumn(
                name: "nat_op",
                table: "documentos_fiscais");

            migrationBuilder.DropColumn(
                name: "nfe_destinatario",
                table: "documentos_fiscais");
        }
    }
}
