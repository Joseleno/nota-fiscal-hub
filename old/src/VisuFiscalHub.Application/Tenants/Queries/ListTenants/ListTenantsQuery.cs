using Mediator;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Application.Tenants.Queries.ListTenants;

public sealed record ListTenantsQuery : IQuery<Result<PagedResult<TenantResponse>>>
{
    public ClienteAppId ClienteAppId { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
