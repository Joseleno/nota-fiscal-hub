namespace VisuFiscalHub.Application.Common.Models;

// WebhookSecret absent — irrecoverable after creation
public sealed record ClienteAppResponse(
    Guid Id,
    string Name,
    string ClientId,
    string? WebhookUrl,
    bool IsActive,
    DateTimeOffset CreatedAt);
