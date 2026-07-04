using System.Globalization;

namespace NotaFiscalHub.BuildingBlocks.Idempotency;

/// <summary>
/// Construção das strings SQL cruas usadas por <see cref="IdempotencyStore"/> — extraídas para métodos
/// estáticos puros para permitir verificação de forma/conteúdo por teste de unidade sem depender de
/// PostgreSQL real (a execução em si só pode ser validada contra um Postgres real — Testcontainers).
///
/// Mesmo padrão de <c>OutboxSql</c> (Tarefa 3): a corrida (spec B4 §Abordagem passos 3/5) é sempre
/// resolvida por UM ÚNICO statement atômico — nunca um SELECT solto seguido de um segundo statement
/// condicional, o que reabriria a janela de corrida entre a leitura e a decisão (TOCTOU).
/// </summary>
internal static class IdempotencySql
{
    /// <summary>
    /// Tenta reivindicar o registro <c>EmProcessamento</c> abandonado (mais velho que
    /// <paramref name="orphanTimeout"/>) para a mesma PK, renovando <c>criada_em</c>/<c>payload_hash</c>
    /// atomicamente. Uma linha afetada = esta chamada venceu o takeover; zero linhas = ou o registro não
    /// está mais órfão (outra chamada já venceu, ou já foi concluído/liberado), ou não existe registro
    /// nessa PK — em ambos os casos o chamador deve reler o estado atual e seguir o fluxo normal de
    /// decisão (spec B4 §Abordagem passo 5).
    /// </summary>
    public static string TakeoverOrfao(string schema, TimeSpan orphanTimeout)
    {
        var tabela = QuoteTabela(schema, "idempotency_registro");
        var segundos = orphanTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture);

        return "UPDATE " + tabela + " " +
               "SET criada_em = now(), payload_hash_sha256 = {0} " +
               "WHERE conta_id = {1} AND ambiente = {2} AND rota = {3} AND key = {4} " +
               "AND estado = 'EmProcessamento' " +
               "AND criada_em < now() - interval '" + segundos + " seconds'";
    }

    private static string QuoteTabela(string schema, string tabela) => $"\"{schema}\".\"{tabela}\"";
}
