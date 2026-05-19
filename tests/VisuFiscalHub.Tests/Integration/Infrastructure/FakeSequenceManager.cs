using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Tests.Integration.Infrastructure;

/// <summary>
/// In-memory stub for ISequenceManager.
/// SequenceManager uses ExecuteSqlRawAsync / SqlQueryRaw which are relational-specific EF methods
/// incompatible with the InMemory provider used in integration tests.
/// </summary>
public sealed class FakeSequenceManager : ISequenceManager
{
    // Interlocked.Increment returns the value AFTER incrementing.
    // Starting at 0 ensures the first call returns 1 (first valid document number).
    private long _next = 0;

    public Task<Result> EnsureNumeracaoSequenceAsync(TenantId tenantId, string serie, CancellationToken ct = default)
        => Task.FromResult(Result.Success());

    public Task<Result<long>> GetNextNumeroAsync(TenantId tenantId, string serie, CancellationToken ct = default)
        => Task.FromResult(Result.Success(Interlocked.Increment(ref _next)));
}
