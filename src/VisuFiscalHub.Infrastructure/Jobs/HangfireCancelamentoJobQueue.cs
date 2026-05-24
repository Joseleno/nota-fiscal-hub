using Hangfire;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Infrastructure.Jobs;

internal sealed class HangfireCancelamentoJobQueue : ICancelamentoJobQueue
{
    private readonly IBackgroundJobClient _jobClient;

    public HangfireCancelamentoJobQueue(IBackgroundJobClient jobClient)
    {
        _jobClient = jobClient;
    }

    public Task EnqueueCancelamentoAsync(DocumentoFiscalId id, string justificativa, CancellationToken ct = default)
    {
        _jobClient.Enqueue<CancelamentoJob>(
            job => job.ExecuteAsync(id, justificativa, CancellationToken.None));
        return Task.CompletedTask;
    }
}
