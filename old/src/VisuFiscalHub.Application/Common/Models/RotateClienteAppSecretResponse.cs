namespace VisuFiscalHub.Application.Common.Models;

public sealed record RotateClienteAppSecretResponse(
    string ClientId,
    string NewClientSecret);
