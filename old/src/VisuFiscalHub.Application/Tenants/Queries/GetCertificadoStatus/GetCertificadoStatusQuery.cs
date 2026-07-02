using Mediator;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Application.Tenants.Queries.GetCertificadoStatus;

public sealed record GetCertificadoStatusQuery : IQuery<Result<CertificadoStatusResponse>>
{
    public TenantId TenantId { get; init; }
    public ClienteAppId ClienteAppId { get; init; }
}
