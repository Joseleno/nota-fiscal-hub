namespace VisuFiscalHub.Application.Common.Models;

public sealed record ClienteAppCreatedResponse(
    Guid Id,
    string Name,
    string ClientId,
    string? WebhookUrl,
    string WebhookSecret,
    bool IsActive,
    DateTimeOffset CreatedAt);
