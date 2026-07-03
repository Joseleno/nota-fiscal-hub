using Microsoft.AspNetCore.Http;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;

namespace NotaFiscalHub.Api.Authentication;

/// <summary>
/// Resolve o tenant da requisição via <see cref="ITenantResolver"/> e abre um
/// <see cref="ITenantScope"/> (<see cref="ITenantScopeFactory.BeginTenantScope"/>) que envolve todo o
/// pipeline downstream — o dispose ocorre sempre ao fim do request (bloco <c>using</c>), garantindo que
/// o <c>AsyncLocal</c> do tenant nunca vaza para a próxima requisição atendida pela mesma thread do pool.
///
/// Endpoints em <see cref="EndpointsSemTenant"/> (health/liveness) não passam pelo middleware: nunca
/// tocam um DbContext tenant-scoped e não devem exigir header de tenant.
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    private static readonly string[] EndpointsSemTenant = ["/alive", "/health"];

    public async Task InvokeAsync(HttpContext context, ITenantResolver resolver, ITenantScopeFactory scopeFactory)
    {
        if (EndpointsSemTenant.Contains(context.Request.Path.Value, StringComparer.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var resolvido = await resolver.ResolveAsync(context);
        if (resolvido is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        using (scopeFactory.BeginTenantScope(resolvido.Value.ContaId))
        {
            await next(context);
        }
    }
}
