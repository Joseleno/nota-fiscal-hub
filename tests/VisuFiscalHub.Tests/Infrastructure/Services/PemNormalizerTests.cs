using System.Security.Cryptography;
using Shouldly;
using Xunit;
using VisuFiscalHub.Infrastructure.Services;

namespace VisuFiscalHub.Tests.Infrastructure.Services;

public sealed class PemNormalizerTests
{
    [Fact]
    public void Normalize_ComNLiteral_ConverteParaNewlineReal()
    {
        using var rsa = RSA.Create(2048);
        var pemReal = rsa.ExportSubjectPublicKeyInfoPem();
        var pemLiteral = pemReal.Replace("\n", "\\n");

        var normalizado = PemNormalizer.Normalize(pemLiteral);

        using var rsaImport = RSA.Create();
        var ex = Record.Exception(() => rsaImport.ImportFromPem(normalizado));
        ex.ShouldBeNull("PEM com \\n literal deve ser normalizável para ImportFromPem");
    }

    [Fact]
    public void Normalize_ComRNLiteral_ConverteParaNewlineReal()
    {
        using var rsa = RSA.Create(2048);
        var pemReal = rsa.ExportSubjectPublicKeyInfoPem();
        var pemLiteral = pemReal.Replace("\n", "\\r\\n");

        var normalizado = PemNormalizer.Normalize(pemLiteral);

        using var rsaImport = RSA.Create();
        var ex = Record.Exception(() => rsaImport.ImportFromPem(normalizado));
        ex.ShouldBeNull("PEM com \\r\\n literal deve ser normalizável para ImportFromPem");
    }

    [Fact]
    public void Normalize_ComNewlineReal_NaoAltera()
    {
        using var rsa = RSA.Create(2048);
        var pemReal = rsa.ExportSubjectPublicKeyInfoPem();

        var normalizado = PemNormalizer.Normalize(pemReal);

        normalizado.ShouldBe(pemReal);
    }

    [Fact]
    public void Normalize_ComRLiteral_ConverteParaNewlineReal()
    {
        using var rsa = RSA.Create(2048);
        var pemReal = rsa.ExportSubjectPublicKeyInfoPem();
        var pemLiteral = pemReal.Replace("\n", "\\r");

        var normalizado = PemNormalizer.Normalize(pemLiteral);

        using var rsaImport = RSA.Create();
        var ex = Record.Exception(() => rsaImport.ImportFromPem(normalizado));
        ex.ShouldBeNull("PEM com \\r literal deve ser normalizável para ImportFromPem");
    }

    [Fact]
    public void Normalize_ChavePrivada_ImportFromPemFunciona()
    {
        using var rsa = RSA.Create(2048);
        var pemReal = rsa.ExportRSAPrivateKeyPem();
        var pemLiteral = pemReal.Replace("\n", "\\n");

        var normalizado = PemNormalizer.Normalize(pemLiteral);

        using var rsaImport = RSA.Create();
        var ex = Record.Exception(() => rsaImport.ImportFromPem(normalizado));
        ex.ShouldBeNull("Chave privada com \\n literal deve ser normalizável");
    }
}
