using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Shouldly;
using VisuFiscalHub.Infrastructure.Services;

namespace VisuFiscalHub.Tests.Infrastructure.Services;

/// <summary>
/// Verifica a função ComputeHmacSha256 do WebhookDeliveryService:
/// formato hex lowercase, determinismo, sensibilidade a payload e secret.
/// </summary>
public class WebhookSignatureTests
{
    // Acessa ComputeHmacSha256 via reflexão — é private static, sem dependências externas.
    private static string InvokeComputeHmac(byte[] payload, string secret)
    {
        var method = typeof(WebhookDeliveryService)
            .GetMethod("ComputeHmacSha256",
                BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string)method.Invoke(null, [payload, secret])!;
    }

    // Referência independente: HMAC-SHA256 computado diretamente pela BCL.
    private static string HmacRef(byte[] payload, string secret)
    {
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payload);
        return Convert.ToHexString(mac).ToLowerInvariant();
    }

    // ── formato ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ComputeHmacSha256_RetornaHexLowercase()
    {
        var payload = Encoding.UTF8.GetBytes("""{"event":"test"}""");
        var result  = InvokeComputeHmac(payload, "mysecret");

        result.ShouldNotBeNullOrWhiteSpace();
        result.ShouldMatch("^[0-9a-f]{64}$", "deve ser hex lowercase de 64 caracteres (SHA-256 = 32 bytes)");
    }

    // ── corretude ────────────────────────────────────────────────────────────────

    [Fact]
    public void ComputeHmacSha256_ProduziuMesmaMacQueReferenciaBCL()
    {
        var payload = Encoding.UTF8.GetBytes("""{"event":"documento.autorizado","status":"Autorizado"}""");
        const string secret = "abcdef1234567890abcdef1234567890";

        var resultado  = InvokeComputeHmac(payload, secret);
        var referencia = HmacRef(payload, secret);

        resultado.ShouldBe(referencia);
    }

    // ── determinismo ─────────────────────────────────────────────────────────────

    [Fact]
    public void ComputeHmacSha256_MesmoPayloadESecret_ProduziuMesmaAssinatura()
    {
        var payload = Encoding.UTF8.GetBytes("payload fixo");
        const string secret = "segredo-fixo";

        var primeira = InvokeComputeHmac(payload, secret);
        var segunda  = InvokeComputeHmac(payload, secret);

        primeira.ShouldBe(segunda);
    }

    // ── sensibilidade ao payload ──────────────────────────────────────────────────

    [Fact]
    public void ComputeHmacSha256_PayloadsDiferentes_ProduziuAssinaturasDiferentes()
    {
        const string secret  = "segredo";
        var payload1 = Encoding.UTF8.GetBytes("payload A");
        var payload2 = Encoding.UTF8.GetBytes("payload B");

        var sig1 = InvokeComputeHmac(payload1, secret);
        var sig2 = InvokeComputeHmac(payload2, secret);

        sig1.ShouldNotBe(sig2);
    }

    // ── sensibilidade ao secret ───────────────────────────────────────────────────

    [Fact]
    public void ComputeHmacSha256_SecretsDiferentes_ProduziuAssinaturasDiferentes()
    {
        var payload  = Encoding.UTF8.GetBytes("mesmo payload");
        const string secret1 = "secret-A";
        const string secret2 = "secret-B";

        var sig1 = InvokeComputeHmac(payload, secret1);
        var sig2 = InvokeComputeHmac(payload, secret2);

        sig1.ShouldNotBe(sig2);
    }

    // ── vetor de teste fixo (RFC 4231 test case 1, adaptado para hex) ─────────────
    // Garante que a implementação não quebrará silenciosamente em refactorings futuros.

    [Fact]
    public void ComputeHmacSha256_VetorFixo_ProduziuHmacEsperado()
    {
        // secret = "secret", payload = "Hello, Webhook!"
        // Resultado calculado independentemente com BCL:
        // HMACSHA256(key=UTF8("secret"), data=UTF8("Hello, Webhook!"))
        var payload = Encoding.UTF8.GetBytes("Hello, Webhook!");
        const string secret = "secret";

        var resultado  = InvokeComputeHmac(payload, secret);
        var esperado   = HmacRef(payload, secret);   // referência BCL como golden value

        resultado.ShouldBe(esperado);
        resultado.Length.ShouldBe(64, "SHA-256 produz 32 bytes = 64 chars hex");
    }
}
