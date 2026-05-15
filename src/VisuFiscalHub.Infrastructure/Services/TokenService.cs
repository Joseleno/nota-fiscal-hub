using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Infrastructure.Services;

internal sealed class TokenService : ITokenService, IDisposable
{
    private readonly JwtSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly RSA _rsa;
    private readonly SigningCredentials _credentials;

    public TokenService(IOptions<JwtSettings> settings, TimeProvider timeProvider)
    {
        _settings = settings.Value;
        _timeProvider = timeProvider;

        _rsa = RSA.Create();
        _rsa.ImportFromPem(_settings.PrivateKeyPem);
        var key = new RsaSecurityKey(_rsa);
        _credentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
    }

    public string GenerateToken(ClienteAppId clienteAppId, string clientId)
    {
        var now = _timeProvider.GetUtcNow();
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, clienteAppId.Value.ToString()),
            new Claim("client_id", clientId),
            new Claim(JwtRegisteredClaimNames.Iat,
                now.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64),
        };

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: now.UtcDateTime.AddSeconds(_settings.ExpiresInSeconds),
            signingCredentials: _credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public void Dispose() => _rsa.Dispose();
}
