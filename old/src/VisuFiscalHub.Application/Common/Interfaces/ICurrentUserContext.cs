using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Application.Common.Interfaces;

public interface ICurrentUserContext
{
    ClienteAppId ClienteAppId { get; }
    TenantId TenantId { get; }
}
