using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VisuFiscalHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCancelado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cancelado_at",
                table: "documentos_fiscais",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cancelado_at",
                table: "documentos_fiscais");
        }
    }
}
