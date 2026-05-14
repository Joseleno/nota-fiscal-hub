using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Errors;
using VisuFiscalHub.Domain.Events;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Domain.Entities;

public sealed class ClienteApp : Entity<ClienteAppId>
{
    private ClienteApp()
    {
        // Para EF Core — inicialização via reflexão
        Name = string.Empty;
        ClientId = string.Empty;
        ClientSecretHash = string.Empty;
    }

    private ClienteApp(
        ClienteAppId id,
        string name,
        string clientId,
        string clientSecretHash,
        string? webhookUrl,
        byte[]? webhookSecretCriptografado,
        DateTimeOffset createdAt) : base(id)
    {
        Name = name;
        ClientId = clientId;
        ClientSecretHash = clientSecretHash;
        WebhookUrl = webhookUrl;
        WebhookSecretCriptografado = webhookSecretCriptografado;
        IsActive = true;
        CreatedAt = createdAt;
    }

    public string Name { get; private set; }
    public string ClientId { get; private set; }
    public string ClientSecretHash { get; private set; }
    public string? WebhookUrl { get; private set; }
    public byte[]? WebhookSecretCriptografado { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Result<ClienteApp> Criar(
        string name,
        string clientId,
        string clientSecretHash,
        TimeProvider timeProvider,
        string? webhookUrl = null,
        byte[]? webhookSecretCriptografado = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<ClienteApp>(ClienteAppErrors.NomeInvalido);

        if (string.IsNullOrWhiteSpace(clientId))
            return Result.Failure<ClienteApp>(ClienteAppErrors.ClientIdInvalido);

        if (string.IsNullOrWhiteSpace(clientSecretHash))
            return Result.Failure<ClienteApp>(ClienteAppErrors.ClientSecretHashInvalido);

        return Result.Success(new ClienteApp(
            ClienteAppId.New(),
            name.Trim(),
            clientId.Trim(),
            clientSecretHash,
            webhookUrl,
            webhookSecretCriptografado,
            timeProvider.GetUtcNow()));
    }

    public void Desativar() => IsActive = false;

    public Result AtualizarClientSecretHash(string novoHash)
    {
        if (string.IsNullOrWhiteSpace(novoHash))
            return Result.Failure(ClienteAppErrors.ClientSecretHashInvalido);

        ClientSecretHash = novoHash;
        return Result.Success();
    }

    public Result AtualizarWebhookSecret(byte[] webhookSecretCriptografado, TimeProvider timeProvider)
    {
        if (webhookSecretCriptografado is null || webhookSecretCriptografado.Length == 0)
            return Result.Failure(ClienteAppErrors.ClientSecretHashInvalido);

        WebhookSecretCriptografado = webhookSecretCriptografado;

        AddDomainEvent(new ClienteAppWebhookSecretRotadoEvent(
            Id,
            Guid.CreateVersion7(),
            timeProvider.GetUtcNow()));

        return Result.Success();
    }
}
