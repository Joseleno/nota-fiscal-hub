using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotaFiscalHub.BuildingBlocks.Auditoria.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "auditoria");

            migrationBuilder.CreateTable(
                name: "registro_auditoria",
                schema: "auditoria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conta_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tipo_evento = table.Column<string>(type: "text", nullable: false),
                    acao = table.Column<string>(type: "text", nullable: false),
                    recurso_tipo = table.Column<string>(type: "text", nullable: false),
                    recurso_id = table.Column<string>(type: "text", nullable: false),
                    ator = table.Column<string>(type: "text", nullable: false),
                    ocorrido_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    registrado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<string>(type: "text", nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dados = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_registro_auditoria", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_registro_auditoria_conta_id_registrado_em",
                schema: "auditoria",
                table: "registro_auditoria",
                columns: new[] { "conta_id", "registrado_em" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_registro_auditoria_message_id",
                schema: "auditoria",
                table: "registro_auditoria",
                column: "message_id",
                unique: true);

            // Imutabilidade em nível de banco (spec B5 §Abordagem passo 3, critério de aceite 3): trigger
            // de bloqueio — UPDATE/DELETE diretos na tabela lançam PostgresException, MESMO por
            // superusuário de aplicação (o trigger roda para qualquer role, salvo BYPASSRLS/superuser real
            // do Postgres, que não é o papel de aplicação).
            migrationBuilder.Sql(
                """
                CREATE FUNCTION auditoria.bloquear_mutacao() RETURNS trigger AS
                $$ BEGIN RAISE EXCEPTION 'registro_auditoria e append-only'; END $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER trg_append_only BEFORE UPDATE OR DELETE ON auditoria.registro_auditoria
                  FOR EACH ROW EXECUTE FUNCTION auditoria.bloquear_mutacao();
                """);

            // REVOKE do privilégio do role de aplicação (spec B5 Assunção A2): o role dedicado `nfh_app`
            // ainda não existe em nenhum ambiente real desta fase (decisão de infra pendente da Fase 0/AWS)
            // — até lá, o TRIGGER acima é a garantia real (por isso o critério de aceite 3 testa o trigger,
            // não o privilégio). Envolvido em bloco condicional para que a migration não aborte em bancos
            // onde o role ainda não foi criado: `REVOKE ... FROM role_inexistente` lança erro duro em
            // Postgres (role must exist), o que quebraria toda a aplicação da migration se executado como
            // statement solto.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'nfh_app') THEN
                        REVOKE UPDATE, DELETE, TRUNCATE ON auditoria.registro_auditoria FROM nfh_app;
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_append_only ON auditoria.registro_auditoria;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS auditoria.bloquear_mutacao();");

            migrationBuilder.DropTable(
                name: "registro_auditoria",
                schema: "auditoria");
        }
    }
}
