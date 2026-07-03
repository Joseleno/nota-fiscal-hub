using System.Globalization;

namespace NotaFiscalHub.BuildingBlocks.Messaging;

/// <summary>
/// Construção das strings SQL cruas usadas pelo <see cref="OutboxDispatcher{TDbContext}"/> — extraídas
/// para métodos estáticos puros para permitir verificação de forma/conteúdo por teste de unidade sem
/// depender de PostgreSQL real (a execução em si, incl. o comportamento de <c>FOR UPDATE SKIP LOCKED</c>
/// e <c>ON CONFLICT DO NOTHING</c>, só pode ser validada contra um Postgres real — Testcontainers).
/// </summary>
internal static class OutboxSql
{
    public static string ReivindicarLote(string schema, TimeSpan duracaoDaPosse)
    {
        var tabela = QuoteTabela(schema, "outbox");
        var segundos = duracaoDaPosse.TotalSeconds.ToString(CultureInfo.InvariantCulture);

        return "UPDATE " + tabela + " AS o " +
               "SET proxima_tentativa_em = now() + interval '" + segundos + " seconds' " +
               "FROM (" +
               "  SELECT id FROM " + tabela + " " +
               "  WHERE status = 'Pendente' AND proxima_tentativa_em <= now() " +
               "  ORDER BY proxima_tentativa_em " +
               "  LIMIT {0} " +
               "  FOR UPDATE SKIP LOCKED" +
               ") AS reivindicadas " +
               "WHERE o.id = reivindicadas.id " +
               "RETURNING o.id";
    }

    public static string InserirInboxSeAusente(string schema)
    {
        var tabela = QuoteTabela(schema, "inbox");

        return "INSERT INTO " + tabela + " (message_id, handler, tipo_evento, processada_em) " +
               "VALUES ({0}, {1}, {2}, now()) " +
               "ON CONFLICT DO NOTHING";
    }

    private static string QuoteTabela(string schema, string tabela) => $"\"{schema}\".\"{tabela}\"";
}
