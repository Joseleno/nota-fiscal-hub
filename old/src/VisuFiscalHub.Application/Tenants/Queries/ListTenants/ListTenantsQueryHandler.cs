using Mediator;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Application.Tenants;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Interfaces;

namespace VisuFiscalHub.Application.Tenants.Queries.ListTenants;

public sealed class ListTenantsQueryHandler
    : IQueryHandler<ListTenantsQuery, Result<PagedResult<TenantResponse>>>
{
    private readonly ITenantRepository _tenantRepository;

    public ListTenantsQueryHandler(ITenantRepository tenantRepository)
    {
        _tenantRepository = tenantRepository;
    }

    public async ValueTask<Result<PagedResult<TenantResponse>>> Handle(
        ListTenantsQuery query,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Min(Math.Max(1, query.PageSize), 100);

        var (tenants, totalCount) = await _tenantRepository.GetPagedByClienteAppIdAsync(
            query.ClienteAppId,
            page,
            pageSize,
            cancellationToken);

        var result = new PagedResult<TenantResponse>(
            tenants.Select(TenantMapper.ToResponse).ToList(),
            totalCount,
            page,
            pageSize);

        return Result.Success(result);
    }
}
