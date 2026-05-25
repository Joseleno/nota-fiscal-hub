using Serilog.Context;
using System.Text.RegularExpressions;

namespace VisuFiscalHub.Api.Middleware;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    private static readonly Regex _safeChars = new(@"[^a-zA-Z0-9\-]", RegexOptions.Compiled);

    public async Task InvokeAsync(HttpContext context)
    {
        var raw = context.Request.Headers["X-Correlation-Id"].FirstOrDefault();
        string correlationId;
        if (raw is { Length: > 0 })
        {
            var sanitized = _safeChars.Replace(raw, "");
            correlationId = sanitized.Length > 0
                ? sanitized[..Math.Min(sanitized.Length, 128)]
                : Guid.NewGuid().ToString("N");
        }
        else
        {
            correlationId = Guid.NewGuid().ToString("N");
        }

        context.Response.Headers["X-Correlation-Id"] = correlationId;
        context.Items["CorrelationId"] = correlationId;

        using var _ = LogContext.PushProperty("CorrelationId", correlationId);
        await next(context);
    }
}
