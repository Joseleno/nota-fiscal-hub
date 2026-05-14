using Mediator;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Application.Tenants.Commands.RotateClienteAppSecret;

public sealed record RotateClienteAppSecretCommand : ICommand<Result<RotateClienteAppSecretResponse>>
{
    public ClienteAppId ClienteAppId { get; init; }
}
