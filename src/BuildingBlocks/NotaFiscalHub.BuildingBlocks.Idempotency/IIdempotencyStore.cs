namespace NotaFiscalHub.BuildingBlocks.Idempotency;

/// <summary>
/// Store persistido de registros de idempotência (spec B4 §Abordagem passo 1). Implementado por
/// <see cref="IdempotencyStore"/> contra <see cref="IdempotencyDbContext"/>.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Tenta inserir um registro <see cref="IdempotencyState.EmProcessamento"/> para a PK
    /// <c>(ContaId, Ambiente, Rota, Key)</c> de <paramref name="novo"/>. Retorna
    /// <see cref="IdempotencyBeginOutcome.Inserted"/> (inclui takeover de órfão) quando o chamador deve
    /// executar o handler, ou o desfecho/registro existente vigente (corrida perdida/replay/conflito).
    /// </summary>
    Task<IdempotencyBeginResult> BeginAsync(IdempotencyRecord novo, CancellationToken ct);

    Task CompleteAsync(
        Guid contaId, string ambiente, string rota, string key,
        int status, string corpo, string contentType, string? location, CancellationToken ct);

    /// <summary>Remove o registro EmProcessamento (falha 5xx/exceção) para permitir nova tentativa real.</summary>
    Task ReleaseAsync(Guid contaId, string ambiente, string rota, string key, CancellationToken ct);
}
