using Mediator;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Application.Tenants;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Errors;
using VisuFiscalHub.Domain.Interfaces;

namespace VisuFiscalHub.Application.Tenants.Queries.GetTenant;

public sealed class GetTenantQueryHandler
    : IQueryHandler<GetTenantQuery, Result<TenantResponse>>
{
    private readonly ITenantRepository _tenantRepository;

    public GetTenantQueryHandler(ITenantRepository tenantRepository)
    {
        _tenantRepository = tenantRepository;
    }

    public async ValueTask<Result<TenantResponse>> Handle(
        GetTenantQuery query,
        CancellationToken cancellationToken)
    {
        var tenant = await _tenantRepository.GetByIdAsync(query.TenantId, cancellationToken);
        if (tenant is null)
            return Result.Failure<TenantResponse>(TenantErrors.NaoEncontrado);

        if (tenant.ClienteAppId != query.ClienteAppId)
            return Result.Failure<TenantResponse>(TenantErrors.NaoPertenceAoClienteApp);

        return Result.Success(TenantMapper.ToResponse(tenant));
    }
}
