using System.Security.Claims;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Api.Authentication;

public sealed class HttpContextCurrentUserContext : ICurrentUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public ClienteAppId ClienteAppId
    {
        get
        {
            var context = _httpContextAccessor.HttpContext
                ?? throw new InvalidOperationException("HttpContext não disponível fora de uma requisição HTTP.");

            var sub = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? context.User.FindFirstValue("sub")
                ?? throw new InvalidOperationException("Claim 'sub' ausente no token JWT.");

            if (!Guid.TryParse(sub, out var id))
                throw new InvalidOperationException($"Claim 'sub' não é um Guid válido: {sub}");

            return new ClienteAppId(id);
        }
    }

    public TenantId TenantId
    {
        get
        {
            var context = _httpContextAccessor.HttpContext
                ?? throw new InvalidOperationException("HttpContext não disponível fora de uma requisição HTTP.");

            if (!context.Items.TryGetValue("TenantContext", out var tenantContextObj)
                || tenantContextObj is not TenantContext tenantContext)
                throw new InvalidOperationException("TenantContext ausente. O TenantValidationMiddleware deve ser executado antes.");

            return tenantContext.TenantId;
        }
    }
}
