using Mediator;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Application.Tenants.Commands.RotateClienteAppWebhookSecret;

public sealed record RotateClienteAppWebhookSecretCommand : ICommand<Result<RotateClienteAppWebhookSecretResponse>>
{
    public ClienteAppId ClienteAppId { get; init; }
}
