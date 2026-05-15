using VisuFiscalHub.Domain.Common;

namespace VisuFiscalHub.Domain.Errors;

public static class ClienteAppErrors
{
    public static readonly Error NaoEncontrado =
        new("ClienteApp.NaoEncontrado", "ClienteApp não encontrado.");

    public static readonly Error ClientIdJaExiste =
        new("ClienteApp.ClientIdJaExiste", "O ClientId informado já está em uso.");

    public static readonly Error Inativo =
        new("ClienteApp.Inativo", "O ClienteApp está inativo.");

    public static readonly Error NomeInvalido =
        new("ClienteApp.NomeInvalido", "O nome do ClienteApp é inválido.");

    public static readonly Error ClientIdInvalido =
        new("ClienteApp.ClientIdInvalido", "O ClientId é inválido.");

    public static readonly Error ClientSecretHashInvalido =
        new("ClienteApp.ClientSecretHashInvalido", "O hash do ClientSecret é inválido.");

    public static readonly Error WebhookSecretInvalido =
        new("ClienteApp.WebhookSecretInvalido", "O webhook secret criptografado é inválido.");
}
