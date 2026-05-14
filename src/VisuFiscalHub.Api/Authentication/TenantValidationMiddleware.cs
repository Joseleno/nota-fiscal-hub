using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.Interfaces;

namespace VisuFiscalHub.Api.Authentication;

public sealed class TenantValidationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITenantRepository tenantRepository)
    {
        if (context.Request.RouteValues.TryGetValue("tenantId", out var tenantIdValue)
            && Guid.TryParse(tenantIdValue?.ToString(), out var tenantGuid))
        {
            var tenantId = new TenantId(tenantGuid);
            var tenant = await tenantRepository.GetByIdAsync(tenantId, context.RequestAborted);

            if (tenant is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsJsonAsync(new { title = "Tenant não encontrado.", status = 404 });
                return;
            }

            context.Items["TenantContext"] = new TenantContext(tenantId);
        }

        await next(context);
    }
}
