namespace VisuFiscalHub.Application.Common.Models;

public sealed record RotateClienteAppWebhookSecretResponse(
    string ClientId,
    string NewWebhookSecret);
