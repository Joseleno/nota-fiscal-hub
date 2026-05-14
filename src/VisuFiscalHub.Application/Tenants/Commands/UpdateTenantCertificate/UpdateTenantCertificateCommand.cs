using Mediator;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Application.Tenants.Commands.UpdateTenantCertificate;

public sealed record UpdateTenantCertificateCommand : ICommand<Result>
{
    public TenantId TenantId { get; init; }
    public ClienteAppId ClienteAppId { get; init; }
    public byte[] PfxBytes { get; init; } = default!;
    public string Senha { get; init; } = default!;
    public DateTimeOffset Vencimento { get; init; }
}
