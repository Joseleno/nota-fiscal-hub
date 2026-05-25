using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VisuFiscalHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCorrelationIdToOutboxMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "correlation_id",
                table: "outbox_messages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "correlation_id",
                table: "outbox_messages");
        }
    }
}
