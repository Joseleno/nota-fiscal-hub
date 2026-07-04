using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using NotaFiscalHub.BuildingBlocks.Auditoria.Migrations;
using Xunit;

namespace NotaFiscalHub.BuildingBlocks.Auditoria.UnitTests;

/// <summary>
/// Mitigação parcial para a ausência de Testcontainers/PostgreSQL neste ambiente (mesmo padrão de
/// <c>OutboxSqlTests</c>, Tarefa 3): inspeciona os statements <c>migrationBuilder.Sql(...)</c> registrados
/// pela migration <c>InitialCreate</c> via reflection sobre <see cref="MigrationBuilder.Operations"/>,
/// sem precisar aplicar a migration contra um banco real. Não substitui <c>ImmutabilityTests.cs</c>
/// (Testcontainers) — que é quem prova que o TRIGGER de fato bloqueia UPDATE/DELETE em tempo de execução —
/// mas garante que o texto do trigger/REVOKE não se perde/quebra silenciosamente por uma futura edição
/// manual da migration.
/// </summary>
public class MigrationSqlTests
{
    [Fact]
    public void InitialCreate_Up_ContemFuncaoDeBloqueioDeMutacao()
    {
        var sqlOperations = ExecutarUpECapturarSqlOperations();

        Assert.Contains(sqlOperations, sql =>
            sql.Contains("CREATE FUNCTION auditoria.bloquear_mutacao()") &&
            sql.Contains("RAISE EXCEPTION"));
    }

    [Fact]
    public void InitialCreate_Up_ContemTriggerAppendOnlyAntesDeUpdateOuDelete()
    {
        var sqlOperations = ExecutarUpECapturarSqlOperations();

        Assert.Contains(sqlOperations, sql =>
            sql.Contains("CREATE TRIGGER trg_append_only BEFORE UPDATE OR DELETE ON auditoria.registro_auditoria") &&
            sql.Contains("EXECUTE FUNCTION auditoria.bloquear_mutacao()"));
    }

    [Fact]
    public void InitialCreate_Up_ContemRevokeDoRoleDeAplicacao()
    {
        var sqlOperations = ExecutarUpECapturarSqlOperations();

        Assert.Contains(sqlOperations, sql =>
            sql.Contains("REVOKE UPDATE, DELETE, TRUNCATE ON auditoria.registro_auditoria FROM nfh_app"));
    }

    [Fact]
    public void InitialCreate_Up_RevokeEnvolvidoEmGuardaDeExistenciaDoRole()
    {
        // Assunção A2 (spec B5): o role nfh_app pode não existir ainda em todo ambiente — o REVOKE precisa
        // estar protegido para não abortar a aplicação da migration inteira.
        var sqlOperations = ExecutarUpECapturarSqlOperations();

        Assert.Contains(sqlOperations, sql =>
            sql.Contains("REVOKE") &&
            sql.Contains("SELECT FROM pg_roles WHERE rolname = 'nfh_app'"));
    }

    [Fact]
    public void InitialCreate_Down_RemoveTriggerEFuncaoAntesDaTabela()
    {
        var migration = new InitialCreate();
        var migrationBuilder = new MigrationBuilder(activeProvider: "Npgsql.EntityFrameworkCore.PostgreSQL");

        InvocarMetodoProtegido(migration, "Down", migrationBuilder);

        var sqlOperations = migrationBuilder.Operations.OfType<SqlOperation>().Select(o => o.Sql).ToList();

        Assert.Contains(sqlOperations, sql => sql.Contains("DROP TRIGGER IF EXISTS trg_append_only"));
        Assert.Contains(sqlOperations, sql => sql.Contains("DROP FUNCTION IF EXISTS auditoria.bloquear_mutacao"));
    }

    private static List<string> ExecutarUpECapturarSqlOperations()
    {
        var migration = new InitialCreate();
        var migrationBuilder = new MigrationBuilder(activeProvider: "Npgsql.EntityFrameworkCore.PostgreSQL");

        InvocarMetodoProtegido(migration, "Up", migrationBuilder);

        return migrationBuilder.Operations.OfType<SqlOperation>().Select(o => o.Sql).ToList();
    }

    private static void InvocarMetodoProtegido(Migration migration, string nomeDoMetodo, MigrationBuilder migrationBuilder)
    {
        var metodo = typeof(Migration).GetMethod(nomeDoMetodo, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Método {nomeDoMetodo} não encontrado em Migration.");

        metodo.Invoke(migration, [migrationBuilder]);
    }
}
