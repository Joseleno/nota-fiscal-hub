using Mediator;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Application.Tenants.Commands.UpdateTenantCsc;

public sealed record UpdateTenantCscCommand : ICommand<Result>
{
    public TenantId TenantId { get; init; }
    public ClienteAppId ClienteAppId { get; init; }
    public string Csc { get; init; } = default!;
    public string CIdToken { get; init; } = default!;
}
