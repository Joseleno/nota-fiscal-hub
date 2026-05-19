using System.Collections.Concurrent;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Tests.Integration.Infrastructure;

/// <summary>
/// No-op stub for IDocumentJobQueue in the integration test environment.
/// Replaces the Hangfire-backed DocumentJobQueue which requires PostgreSQL storage.
/// Enqueued document IDs are recorded so tests can inspect them if needed.
/// </summary>
public sealed class FakeDocumentJobQueue : IDocumentJobQueue
{
    // ConcurrentQueue models the FIFO ordering of the real Hangfire-backed queue.
    // Reset() drains the queue between tests rather than calling Clear() on a ConcurrentBag,
    // which would race with concurrent Enqueue calls on the same thread-pool thread.
    private readonly ConcurrentQueue<DocumentoFiscalId> _enqueuedIds = new();

    public IReadOnlyCollection<DocumentoFiscalId> EnqueuedIds => _enqueuedIds.ToArray();

    public void Reset()
    {
        while (_enqueuedIds.TryDequeue(out _)) { }
    }

    public Task EnqueueProcessingAsync(DocumentoFiscalId id, CancellationToken ct = default)
    {
        _enqueuedIds.Enqueue(id);
        return Task.CompletedTask;
    }
}
