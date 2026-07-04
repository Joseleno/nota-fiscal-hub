using NotaFiscalHub.BuildingBlocks.Auditoria;
using Xunit;

namespace NotaFiscalHub.BuildingBlocks.Auditoria.UnitTests;

/// <summary>
/// Verificação de forma/conteúdo do SQL cru do insert de dedupe (sem PostgreSQL real — mesmo padrão de
/// <c>OutboxSqlTests</c>/<c>IdempotencySqlTests</c>). Pega regressão óbvia (alguém reintroduzindo
/// check-then-insert) mesmo sem banco.
/// </summary>
public class AuditoriaSqlTests
{
    [Fact]
    public void InserirRegistroSeAusente_UsaOnConflictDoNothing_NaoCheckThenInsert()
    {
        var sql = AuditoriaSql.InserirRegistroSeAusente("auditoria");

        Assert.StartsWith("INSERT INTO", sql);
        Assert.Contains("ON CONFLICT (message_id) DO NOTHING", sql);
    }

    [Fact]
    public void InserirRegistroSeAusente_QualificaTabelaComSchemaInformado()
    {
        var sql = AuditoriaSql.InserirRegistroSeAusente("auditoria");

        Assert.Contains("\"auditoria\".\"registro_auditoria\"", sql);
    }

    [Fact]
    public void InserirRegistroSeAusente_TemDozePlaceholdersDeParametro()
    {
        var sql = AuditoriaSql.InserirRegistroSeAusente("auditoria");

        for (var i = 0; i < 12; i++)
        {
            Assert.Contains($"{{{i}}}", sql);
        }

        Assert.DoesNotContain("{12}", sql);
    }
}
