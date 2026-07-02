using Mediator;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Domain.Common;

namespace VisuFiscalHub.Application.Tenants.Commands.CreateClienteApp;

public sealed record CreateClienteAppCommand : ICommand<Result<ClienteAppCreatedResponse>>
{
    public string Name { get; init; } = default!;
    public string ClientId { get; init; } = default!;
    public string ClientSecret { get; init; } = default!;
    public string? WebhookUrl { get; init; }
}
