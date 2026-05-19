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
    private long _next = 1;

    public Task<Result> EnsureNumeracaoSequenceAsync(TenantId tenantId, string serie, CancellationToken ct = default)
        => Task.FromResult(Result.Success());

    public Task<Result<long>> GetNextNumeroAsync(TenantId tenantId, string serie, CancellationToken ct = default)
        => Task.FromResult(Result.Success(Interlocked.Increment(ref _next)));
}
