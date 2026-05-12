using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.ValueObjects;

namespace VisuFiscalHub.Application.Common.Interfaces;

public interface ITenantCertificateProvider
{
    Task<CertificadoDigital> GetCertificateAsync(TenantId tenantId, CancellationToken ct = default);
}
