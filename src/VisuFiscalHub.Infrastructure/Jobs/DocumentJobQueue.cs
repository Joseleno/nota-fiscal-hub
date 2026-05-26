using Hangfire;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Infrastructure.Jobs;

internal sealed class DocumentJobQueue : IDocumentJobQueue
{
    private readonly IBackgroundJobClient _jobClient;

    public DocumentJobQueue(IBackgroundJobClient jobClient)
    {
        _jobClient = jobClient;
    }

    public Task EnqueueProcessingAsync(DocumentoFiscalId id, TipoDocumento tipo, CancellationToken ct = default)
    {
        if (tipo == TipoDocumento.NFSe)
            _jobClient.Enqueue<PrefeituraProcessingJob>(job => job.ExecuteAsync(id, CancellationToken.None));
        else
            _jobClient.Enqueue<FiscalDocumentProcessingJob>(job => job.ExecuteAsync(id, CancellationToken.None));

        return Task.CompletedTask;
    }
}
