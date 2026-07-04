using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotaFiscalHub.BuildingBlocks.Idempotency.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "kernel");

            migrationBuilder.CreateTable(
                name: "idempotency_registro",
                schema: "kernel",
                columns: table => new
                {
                    conta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ambiente = table.Column<string>(type: "text", nullable: false),
                    key = table.Column<string>(type: "text", nullable: false),
                    rota = table.Column<string>(type: "text", nullable: false),
                    payload_hash_sha256 = table.Column<string>(type: "text", nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false),
                    resposta_status = table.Column<int>(type: "integer", nullable: true),
                    resposta_corpo = table.Column<string>(type: "text", nullable: true),
                    resposta_content_type = table.Column<string>(type: "text", nullable: true),
                    resposta_location = table.Column<string>(type: "text", nullable: true),
                    criada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expira_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_registro", x => new { x.conta_id, x.ambiente, x.rota, x.key });
                });

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_registro_expira_em",
                schema: "kernel",
                table: "idempotency_registro",
                column: "expira_em");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idempotency_registro",
                schema: "kernel");
        }
    }
}
