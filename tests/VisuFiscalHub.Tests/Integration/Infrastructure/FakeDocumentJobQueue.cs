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
    private readonly ConcurrentBag<DocumentoFiscalId> _enqueuedIds = [];

    public IReadOnlyCollection<DocumentoFiscalId> EnqueuedIds => _enqueuedIds;

    public void Reset() => _enqueuedIds.Clear();

    public Task EnqueueProcessingAsync(DocumentoFiscalId id, CancellationToken ct = default)
    {
        _enqueuedIds.Add(id);
        return Task.CompletedTask;
    }
}
