using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Hangfire.Dashboard;
using Microsoft.Extensions.Options;
using VisuFiscalHub.Application.Common.Models;

namespace VisuFiscalHub.Api.Authentication;

public sealed class HangfireDashboardAuthFilter(
    IOptions<HangfireDashboardSettings> settings,
    IWebHostEnvironment env) : IDashboardAuthorizationFilter
{
    private static readonly byte[] _hmacKey = RandomNumberGenerator.GetBytes(32);

    public bool Authorize(DashboardContext context)
    {
        // Em Development não exige autenticação — facilita depuração local
        if (env.IsDevelopment())
            return true;

        var httpContext = context.GetHttpContext();

        if (!AuthenticationHeaderValue.TryParse(
                httpContext.Request.Headers.Authorization,
                out var header)
            || !string.Equals(header.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)
            || header.Parameter is null)
        {
            Challenge(httpContext);
            return false;
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter));
        }
        catch
        {
            Challenge(httpContext);
            return false;
        }

        var separatorIndex = decoded.IndexOf(':');
        if (separatorIndex < 0)
        {
            Challenge(httpContext);
            return false;
        }

        var user = decoded[..separatorIndex];
        var password = decoded[(separatorIndex + 1)..];

        var enc = Encoding.UTF8;
        var userMatch = CryptographicOperations.FixedTimeEquals(
            HMACSHA256.HashData(_hmacKey, enc.GetBytes(user)),
            HMACSHA256.HashData(_hmacKey, enc.GetBytes(settings.Value.User)));

        var passwordMatch = CryptographicOperations.FixedTimeEquals(
            HMACSHA256.HashData(_hmacKey, enc.GetBytes(password)),
            HMACSHA256.HashData(_hmacKey, enc.GetBytes(settings.Value.Password)));

        if (userMatch && passwordMatch)
            return true;

        Challenge(httpContext);
        return false;
    }

    private static void Challenge(HttpContext ctx)
    {
        ctx.Response.StatusCode = 401;
        ctx.Response.Headers.WWWAuthenticate = "Basic realm=\"Hangfire Dashboard\"";
    }
}
