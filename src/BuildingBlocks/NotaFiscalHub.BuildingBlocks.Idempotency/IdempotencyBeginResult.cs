namespace NotaFiscalHub.BuildingBlocks.Idempotency;

/// <summary>Resultado de <see cref="IIdempotencyStore.BeginAsync"/> (spec B4 §Abordagem passo 3).</summary>
public enum IdempotencyBeginOutcome
{
    /// <summary>Registro novo inserido (inclui takeover de órfão) — o chamador deve executar o handler.</summary>
    Inserted,

    /// <summary>Registro existente <see cref="IdempotencyState.Concluida"/> com hash igual — devolver a resposta armazenada.</summary>
    ReplayConcluida,

    /// <summary>Registro existente (qualquer estado) com hash diferente — 409 <c>idempotency_key_conflito</c>.</summary>
    ConflitoHashDiferente,

    /// <summary>Registro existente <see cref="IdempotencyState.EmProcessamento"/> com hash igual, ainda dentro do OrphanTimeout — 409 <c>requisicao_em_processamento</c>.</summary>
    CorridaEmProcessamento,
}

public sealed record IdempotencyBeginResult(IdempotencyBeginOutcome Outcome, IdempotencyRecord? Existente);
