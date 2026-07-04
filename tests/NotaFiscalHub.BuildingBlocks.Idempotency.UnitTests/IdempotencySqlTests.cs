namespace NotaFiscalHub.BuildingBlocks.Idempotency.UnitTests;

/// <summary>
/// Verifica forma/conteúdo do SQL de takeover de órfão sem depender de PostgreSQL real (spec B4 §Abordagem
/// passo 5) — mesmo idioma de <c>OutboxSqlTests</c> (Tarefa 3): o statement precisa ser um único
/// <c>UPDATE</c> atômico condicionado a <c>estado = 'EmProcessamento'</c> e à janela do OrphanTimeout, não
/// um SELECT solto seguido de um segundo passo.
/// </summary>
public class IdempotencySqlTests
{
    [Fact]
    public void TakeoverOrfao_EhUmUnicoUpdateAtomico_NaoApenasSelectSolto()
    {
        var sql = IdempotencySql.TakeoverOrfao("kernel", TimeSpan.FromSeconds(60));

        Assert.StartsWith("UPDATE", sql.TrimStart());
        Assert.DoesNotContain("SELECT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TakeoverOrfao_CondicionaEstadoEmProcessamento()
    {
        var sql = IdempotencySql.TakeoverOrfao("kernel", TimeSpan.FromSeconds(60));

        Assert.Contains("estado = 'EmProcessamento'", sql);
    }

    [Fact]
    public void TakeoverOrfao_CondicionaJanelaDoOrphanTimeout()
    {
        var sql = IdempotencySql.TakeoverOrfao("kernel", TimeSpan.FromSeconds(60));

        Assert.Contains("criada_em < now() - interval '60 seconds'", sql);
    }

    [Fact]
    public void TakeoverOrfao_QuotaSchemaETabela()
    {
        var sql = IdempotencySql.TakeoverOrfao("kernel", TimeSpan.FromSeconds(60));

        Assert.Contains("\"kernel\".\"idempotency_registro\"", sql);
    }

    [Fact]
    public void TakeoverOrfao_FiltraPelaPkComposta()
    {
        var sql = IdempotencySql.TakeoverOrfao("kernel", TimeSpan.FromSeconds(60));

        Assert.Contains("conta_id = {1}", sql);
        Assert.Contains("ambiente = {2}", sql);
        Assert.Contains("rota = {3}", sql);
        Assert.Contains("key = {4}", sql);
    }
}
