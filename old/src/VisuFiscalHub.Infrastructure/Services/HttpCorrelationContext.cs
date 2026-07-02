using Microsoft.AspNetCore.Http;
using VisuFiscalHub.Application.Common.Interfaces;

namespace VisuFiscalHub.Infrastructure.Services;

internal sealed class HttpCorrelationContext(IHttpContextAccessor accessor) : ICorrelationContext
{
    public string? CorrelationId =>
        accessor.HttpContext?.Items["CorrelationId"] as string;
}
