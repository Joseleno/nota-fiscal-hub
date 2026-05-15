using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Infrastructure.Services;

internal sealed class TokenService : ITokenService
{
    private readonly JwtSettings _settings;
    private readonly TimeProvider _timeProvider;

    public TokenService(IOptions<JwtSettings> settings, TimeProvider timeProvider)
    {
        _settings = settings.Value;
        _timeProvider = timeProvider;
    }

    public string GenerateToken(ClienteAppId clienteAppId, string clientId)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(_settings.PrivateKeyPem);

        var key = new RsaSecurityKey(rsa);
        var credentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);

        var now = _timeProvider.GetUtcNow();
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, clienteAppId.Value.ToString()),
            new Claim("client_id", clientId),
            new Claim(JwtRegisteredClaimNames.Iat,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64),
        };

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: now.UtcDateTime.AddSeconds(_settings.ExpiresInSeconds),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
