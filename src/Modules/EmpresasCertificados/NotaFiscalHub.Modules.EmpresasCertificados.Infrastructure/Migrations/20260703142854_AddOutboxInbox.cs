using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotaFiscalHub.Modules.EmpresasCertificados.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "empresas");

            migrationBuilder.CreateTable(
                name: "inbox",
                schema: "empresas",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    handler = table.Column<string>(type: "text", nullable: false),
                    tipo_evento = table.Column<string>(type: "text", nullable: false),
                    processada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox", x => new { x.message_id, x.handler });
                });

            migrationBuilder.CreateTable(
                name: "outbox",
                schema: "empresas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_evento = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    conta_id = table.Column<Guid>(type: "uuid", nullable: true),
                    correlation_id = table.Column<string>(type: "text", nullable: false),
                    ocorrido_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    tentativas = table.Column<int>(type: "integer", nullable: false),
                    proxima_tentativa_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    erro_ultimo = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_status_proxima_tentativa_em",
                schema: "empresas",
                table: "outbox",
                columns: new[] { "status", "proxima_tentativa_em" },
                filter: "status = 'Pendente'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbox",
                schema: "empresas");

            migrationBuilder.DropTable(
                name: "outbox",
                schema: "empresas");
        }
    }
}
