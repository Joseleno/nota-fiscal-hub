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
    // ConcurrentQueue modela a ordenação FIFO da fila real do Hangfire.
    // Reset() drena via TryDequeue para garantir que itens enfileirados por chamadas
    // concorrentes não escapem para o próximo teste antes de o loop terminar.
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
