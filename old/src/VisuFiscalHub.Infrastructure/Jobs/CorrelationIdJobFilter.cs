using Hangfire.Server;
using Serilog.Context;
using VisuFiscalHub.Infrastructure.Persistence;

namespace VisuFiscalHub.Infrastructure.Jobs;

internal sealed class CorrelationIdJobFilter : IServerFilter
{
    private static readonly string _scopeKey = nameof(CorrelationIdJobFilter) + ".Scope";

    public void OnPerforming(PerformingContext context)
    {
        var correlationId = context.BackgroundJob.Id;
        CorrelationIdAmbient.Set(correlationId);
        var scope = LogContext.PushProperty("CorrelationId", correlationId);
        context.Items[_scopeKey] = scope;
    }

    public void OnPerformed(PerformedContext context)
    {
        CorrelationIdAmbient.Set(null);
        if (context.Items.TryGetValue(_scopeKey, out var scope))
            ((IDisposable)scope).Dispose();
    }
}
