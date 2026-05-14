using Mediator;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Errors;
using VisuFiscalHub.Domain.Interfaces;

namespace VisuFiscalHub.Application.Tenants.Queries.GetCertificadoStatus;

public sealed class GetCertificadoStatusQueryHandler
    : IQueryHandler<GetCertificadoStatusQuery, Result<CertificadoStatusResponse>>
{
    private readonly ITenantRepository _tenantRepository;
    private readonly TimeProvider _timeProvider;

    public GetCertificadoStatusQueryHandler(
        ITenantRepository tenantRepository,
        TimeProvider timeProvider)
    {
        _tenantRepository = tenantRepository;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<CertificadoStatusResponse>> Handle(
        GetCertificadoStatusQuery query,
        CancellationToken cancellationToken)
    {
        var tenant = await _tenantRepository.GetByIdAsync(query.TenantId, cancellationToken);
        if (tenant is null)
            return Result.Failure<CertificadoStatusResponse>(TenantErrors.NaoEncontrado);

        if (tenant.ClienteAppId != query.ClienteAppId)
            return Result.Failure<CertificadoStatusResponse>(TenantErrors.NaoPertenceAoClienteApp);

        if (tenant.CertificadoVencimento is null)
            return Result.Success(new CertificadoStatusResponse(null, null, false));

        var agora = _timeProvider.GetUtcNow();
        var diasRestantes = Math.Max(0, (int)Math.Floor((tenant.CertificadoVencimento.Value - agora).TotalDays));

        return Result.Success(new CertificadoStatusResponse(
            tenant.CertificadoVencimento,
            diasRestantes,
            true));
    }
}
