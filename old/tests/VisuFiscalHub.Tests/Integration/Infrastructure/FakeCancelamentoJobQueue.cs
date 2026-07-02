using System.Collections.Concurrent;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Tests.Integration.Infrastructure;

/// <summary>
/// No-op stub for ICancelamentoJobQueue in the integration test environment.
/// Replaces the Hangfire-backed HangfireCancelamentoJobQueue which requires PostgreSQL storage.
/// Enqueued cancellation IDs are recorded so tests can inspect them if needed.
/// </summary>
public sealed class FakeCancelamentoJobQueue : ICancelamentoJobQueue
{
    private readonly ConcurrentQueue<(DocumentoFiscalId Id, string Justificativa)> _enqueuedItems = new();

    public IReadOnlyCollection<(DocumentoFiscalId Id, string Justificativa)> EnqueuedItems
        => _enqueuedItems.ToArray();

    public void Reset()
    {
        while (_enqueuedItems.TryDequeue(out _)) { }
    }

    public Task EnqueueCancelamentoAsync(
        DocumentoFiscalId id,
        string justificativa,
        CancellationToken ct = default)
    {
        _enqueuedItems.Enqueue((id, justificativa));
        return Task.CompletedTask;
    }
}
