using NotaFiscalHub.BuildingBlocks.Messaging;

namespace NotaFiscalHub.BuildingBlocks.Messaging.UnitTests;

/// <summary>
/// Verificação de forma/conteúdo das strings SQL cruas do dispatcher (sem PostgreSQL real — a execução
/// em si, incl. o comportamento de <c>FOR UPDATE SKIP LOCKED</c>/<c>ON CONFLICT DO NOTHING</c>, só é
/// validável contra Postgres real via Testcontainers, ver <c>OutboxConcurrencyTests</c>/<c>InboxDedupeTests</c>).
/// Existe para pegar erros de sintaxe/typo de composição de string antes de bater num Postgres real —
/// esses testes teriam detectado, por exemplo, o bug original de <c>SELECT ... FOR UPDATE</c> solto (sem
/// UPDATE atômico) que não fecha a corrida entre dois dispatchers concorrentes.
/// </summary>
public class OutboxSqlTests
{
    [Fact]
    public void ReivindicarLote_ContemUpdateAtomico_NaoApenasSelectSolto()
    {
        var sql = OutboxSql.ReivindicarLote("contas", TimeSpan.FromSeconds(30));

        // A reivindicação precisa ser um UPDATE que já grava a posse — um SELECT ... FOR UPDATE solto
        // (sem UPDATE) teria seu lock liberado antes do dispatcher processar a primeira mensagem.
        Assert.StartsWith("UPDATE", sql);
        Assert.Contains("FOR UPDATE SKIP LOCKED", sql);
        Assert.Contains("RETURNING", sql);
    }

    [Fact]
    public void ReivindicarLote_QualificaTabelaComSchemaInformado()
    {
        var sql = OutboxSql.ReivindicarLote("emissao", TimeSpan.FromSeconds(30));

        Assert.Contains("\"emissao\".\"outbox\"", sql);
    }

    [Fact]
    public void ReivindicarLote_FiltraPorStatusPendenteEProximaTentativaVencida()
    {
        var sql = OutboxSql.ReivindicarLote("contas", TimeSpan.FromSeconds(30));

        Assert.Contains("status = 'Pendente'", sql);
        Assert.Contains("proxima_tentativa_em <= now()", sql);
    }

    [Fact]
    public void ReivindicarLote_TemExatamenteUmPlaceholderDeParametro()
    {
        var sql = OutboxSql.ReivindicarLote("contas", TimeSpan.FromSeconds(30));

        Assert.Contains("LIMIT {0}", sql);
        Assert.DoesNotContain("{1}", sql);
    }

    [Fact]
    public void ReivindicarLote_UsaDuracaoDaPosseInformadaNoIntervalo()
    {
        var sql = OutboxSql.ReivindicarLote("contas", TimeSpan.FromSeconds(45));

        Assert.Contains("interval '45 seconds'", sql);
    }

    [Fact]
    public void InserirInboxSeAusente_UsaOnConflictDoNothing_NaoCheckThenInsert()
    {
        var sql = OutboxSql.InserirInboxSeAusente("contas");

        Assert.Contains("ON CONFLICT DO NOTHING", sql);
        Assert.StartsWith("INSERT INTO", sql);
    }

    [Fact]
    public void InserirInboxSeAusente_QualificaTabelaComSchemaInformado()
    {
        var sql = OutboxSql.InserirInboxSeAusente("documentos");

        Assert.Contains("\"documentos\".\"inbox\"", sql);
    }

    [Fact]
    public void InserirInboxSeAusente_TemExatamenteTresPlaceholdersDeParametro()
    {
        var sql = OutboxSql.InserirInboxSeAusente("contas");

        Assert.Contains("{0}", sql);
        Assert.Contains("{1}", sql);
        Assert.Contains("{2}", sql);
        Assert.DoesNotContain("{3}", sql);
    }
}
