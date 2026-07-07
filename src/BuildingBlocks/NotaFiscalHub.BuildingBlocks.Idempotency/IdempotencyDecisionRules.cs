namespace NotaFiscalHub.BuildingBlocks.Idempotency;

/// <summary>
/// Regra de decisão pura de <see cref="IdempotencyStore.BeginAsync"/> (spec B4 §Abordagem passo 3, mesmo
/// espírito de <c>OutboxDispatchRules</c> — Tarefa 3): dado o registro existente lido (ou nenhum) e o hash
/// do payload novo, decide entre Inserted/ReplayConcluida/ConflitoHashDiferente/CorridaEmProcessamento SEM
/// tocar banco. A resolução real da corrida entre dois <c>INSERT</c>/<c>UPDATE</c> concorrentes cabe à
/// unicidade da PK no banco (ver <see cref="IdempotencyStore.BeginAsync"/>), não a esta função — aqui só
/// se decide o que fazer a partir de uma leitura já feita.
/// </summary>
public static class IdempotencyDecisionRules
{
    /// <param name="existente">Registro lido para a mesma PK, ou <see langword="null"/> se não existe.</param>
    /// <param name="hashNovo">Hash SHA-256 do corpo da requisição atual.</param>
    /// <param name="orfaoVencido">
    /// <see langword="true"/> quando <paramref name="existente"/> está <see cref="IdempotencyState.EmProcessamento"/>
    /// e <c>CriadaEm</c> é mais velho que o <c>OrphanTimeout</c> configurado — nesse caso o registro é
    /// tratado como abandonado e reaproveitado (takeover) independentemente do hash armazenado (spec B4
    /// §Abordagem passo 5): o crash anterior nunca produziu uma resposta para comparar.
    /// </param>
    /// <param name="agora">
    /// Relógio da chamada (spec B4 §Abordagem passo 4/critério de aceite 8): um <paramref name="existente"/>
    /// com <c>ExpiraEm</c> vencido é tratado como se não existisse — o handler executa de novo IMEDIATAMENTE,
    /// sem depender do <c>IdempotencyExpirationJob</c> (que roda de hora em hora) já ter deletado a linha.
    /// </param>
    public static IdempotencyBeginResult Decidir(IdempotencyRecord? existente, string hashNovo, bool orfaoVencido, DateTimeOffset agora)
    {
        if (existente is null || existente.ExpiraEm <= agora)
            return new IdempotencyBeginResult(IdempotencyBeginOutcome.Inserted, null);

        if (existente.Estado == IdempotencyState.EmProcessamento && orfaoVencido)
            return new IdempotencyBeginResult(IdempotencyBeginOutcome.Inserted, null);

        if (existente.PayloadHashSha256 != hashNovo)
            return new IdempotencyBeginResult(IdempotencyBeginOutcome.ConflitoHashDiferente, existente);

        return existente.Estado switch
        {
            IdempotencyState.Concluida => new IdempotencyBeginResult(IdempotencyBeginOutcome.ReplayConcluida, existente),
            IdempotencyState.EmProcessamento => new IdempotencyBeginResult(IdempotencyBeginOutcome.CorridaEmProcessamento, existente),
            _ => throw new InvalidOperationException($"IdempotencyState não tratado: {existente.Estado}"),
        };
    }
}
