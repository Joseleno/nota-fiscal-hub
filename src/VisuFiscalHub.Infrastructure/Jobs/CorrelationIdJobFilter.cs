using Hangfire.Server;
using Serilog.Context;

namespace VisuFiscalHub.Infrastructure.Jobs;

internal sealed class CorrelationIdJobFilter : IServerFilter
{
    private static readonly string _scopeKey = nameof(CorrelationIdJobFilter) + ".Scope";

    public void OnPerforming(PerformingContext context)
    {
        var correlationId = context.BackgroundJob.Id;
        var scope = LogContext.PushProperty("CorrelationId", correlationId);
        context.Items[_scopeKey] = scope;
    }

    public void OnPerformed(PerformedContext context)
    {
        if (context.Items.TryGetValue(_scopeKey, out var scope))
            ((IDisposable)scope).Dispose();
    }
}
