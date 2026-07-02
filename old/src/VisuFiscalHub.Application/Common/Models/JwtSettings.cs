namespace VisuFiscalHub.Application.Common.Models;

public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    public string PrivateKeyPem { get; init; } = string.Empty;
    public string[] PublicKeyPems { get; init; } = [];
    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public int ExpiresInSeconds { get; init; } = 3600;
}
