using Hangfire;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Infrastructure.Jobs;

internal sealed class DocumentJobQueue : IDocumentJobQueue
{
    private readonly IBackgroundJobClient _jobClient;

    public DocumentJobQueue(IBackgroundJobClient jobClient)
    {
        _jobClient = jobClient;
    }

    public Task EnqueueProcessingAsync(DocumentoFiscalId id, CancellationToken ct = default)
    {
        // Enfileira o job de processamento SEFAZ via Hangfire (fire-and-forget).
        // O job é persistido no PostgreSQL — sobrevive a restarts do processo.
        _jobClient.Enqueue<NfceProcessingJob>(job => job.ExecuteAsync(id, CancellationToken.None));
        return Task.CompletedTask;
    }
}
