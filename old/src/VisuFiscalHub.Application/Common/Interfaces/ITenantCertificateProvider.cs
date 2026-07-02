using System.Security.Cryptography.X509Certificates;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Application.Common.Interfaces;

public interface ITenantCertificateProvider
{
    Task<Result<X509Certificate2>> GetCertificateAsync(TenantId tenantId, CancellationToken ct = default);
}
