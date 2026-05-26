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
    // RSAParameters é imutável após exportação — seguro para leitura concorrente em Singleton.
    // Uma nova instância RSA é criada por chamada para evitar problemas de thread-safety
    // do RSAOpenSsl no Linux (RSACng no Windows serializa internamente, mas RSAOpenSsl não garante).
    private readonly RSAParameters _keyParams;

    public TokenService(IOptions<JwtSettings> settings, TimeProvider timeProvider)
    {
        _settings = settings.Value;
        _timeProvider = timeProvider;

        using var rsa = RSA.Create();
        rsa.ImportFromPem(_settings.PrivateKeyPem.Replace("\\n", "\n"));
        _keyParams = rsa.ExportParameters(includePrivateParameters: true);
    }

    public string GenerateToken(ClienteAppId clienteAppId, string clientId)
    {
        using var rsa = RSA.Create();
        rsa.ImportParameters(_keyParams);
        // Disable signature-provider caching so that the CryptoProviderFactory does not
        // retain a reference to 'rsa' after WriteToken() returns and the using-scope disposes it.
        // Without this, the second GenerateToken call reuses a cached SignatureProvider that
        // holds a reference to the first (now-disposed) RSA instance, causing ObjectDisposedException.
        var signingKey = new RsaSecurityKey(rsa)
        {
            CryptoProviderFactory = new Microsoft.IdentityModel.Tokens.CryptoProviderFactory
            {
                CacheSignatureProviders = false
            }
        };
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256);

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
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
