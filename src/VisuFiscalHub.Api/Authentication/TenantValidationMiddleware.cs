using System.Security.Claims;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.Interfaces;

namespace VisuFiscalHub.Api.Authentication;

public sealed class TenantValidationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITenantRepository tenantRepository)
    {
        // Only validate tenant header on authenticated routes that need tenant context.
        // Routes without the header (e.g. /auth/token, /api/v1/clientes) skip this.
        if (!context.Request.Headers.TryGetValue("X-Tenant-Id", out var tenantIdHeader)
            || !Guid.TryParse(tenantIdHeader.FirstOrDefault(), out var tenantGuid))
        {
            await next(context);
            return;
        }

        var tenantId = new TenantId(tenantGuid);
        var tenant = await tenantRepository.GetByIdAsync(tenantId, context.RequestAborted);

        if (tenant is null || !tenant.IsActive)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new { title = "Tenant não encontrado.", status = 404 });
            return;
        }

        // Validate that this tenant belongs to the authenticated ClienteApp.
        var sub = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub");

        if (sub is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { title = "Autenticação necessária.", status = 401 });
            return;
        }

        if (!Guid.TryParse(sub, out var clienteAppGuid))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { title = "Token inválido.", status = 401 });
            return;
        }

        var clienteAppId = new ClienteAppId(clienteAppGuid);
        if (tenant.ClienteAppId != clienteAppId)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { title = "Tenant não pertence a este ClienteApp.", status = 403 });
            return;
        }

        context.Items["TenantContext"] = new TenantContext(tenantId);

        await next(context);
    }
}
