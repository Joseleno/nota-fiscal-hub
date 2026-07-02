using System.Collections.Concurrent;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Tests.Integration.Infrastructure;

/// <summary>
/// No-op stub for IDocumentJobQueue in the integration test environment.
/// Replaces the Hangfire-backed DocumentJobQueue which requires PostgreSQL storage.
/// Enqueued document IDs and tipos are recorded so tests can inspect them if needed.
/// </summary>
public sealed class FakeDocumentJobQueue : IDocumentJobQueue
{
    private readonly ConcurrentQueue<DocumentoFiscalId> _enqueuedIds = new();
    private readonly ConcurrentQueue<TipoDocumento> _enqueuedTipos = new();

    public IReadOnlyCollection<DocumentoFiscalId> EnqueuedIds => _enqueuedIds.ToArray();
    public IReadOnlyCollection<TipoDocumento> EnqueuedTipos => _enqueuedTipos.ToArray();

    public void Reset()
    {
        while (_enqueuedIds.TryDequeue(out _)) { }
        while (_enqueuedTipos.TryDequeue(out _)) { }
    }

    public Task EnqueueProcessingAsync(DocumentoFiscalId id, TipoDocumento tipo, CancellationToken ct = default)
    {
        _enqueuedIds.Enqueue(id);
        _enqueuedTipos.Enqueue(tipo);
        return Task.CompletedTask;
    }
}
