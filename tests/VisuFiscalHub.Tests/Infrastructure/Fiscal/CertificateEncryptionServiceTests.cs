using Microsoft.Extensions.Configuration;
using Shouldly;
using VisuFiscalHub.Infrastructure.Fiscal.Certificates;

namespace VisuFiscalHub.Tests.Infrastructure.Fiscal;

public class CertificateEncryptionServiceTests
{
    private static CertificateEncryptionService CriarService()
    {
        var key32Bytes = Convert.ToBase64String(new byte[32]);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CERT:EncryptionKey"] = key32Bytes
            })
            .Build();
        return new CertificateEncryptionService(config);
    }

    [Fact]
    public void EncryptDecrypt_RoundTrip_DeveRecuperarDadosOriginais()
    {
        var service = CriarService();
        var dados = "conteúdo do certificado pfx simulado 12345"u8.ToArray();

        var encrypted = service.Encrypt(dados).Value;
        var decrypted = service.Decrypt(encrypted).Value;

        decrypted.ShouldBe(dados);
    }

    [Fact]
    public void EncryptString_DecryptToString_RoundTrip()
    {
        var service = CriarService();
        var texto = "senha-do-certificado-pfx";

        var encrypted = service.EncryptString(texto).Value;
        var decrypted = service.DecryptToString(encrypted).Value;

        decrypted.ShouldBe(texto);
    }

    [Fact]
    public void Encrypt_DeveProduzirDadosDiferentesDosOriginais()
    {
        var service = CriarService();
        var dados = "texto claro"u8.ToArray();

        var encrypted = service.Encrypt(dados).Value;

        encrypted.ShouldNotBe(dados);
    }

    [Fact]
    public void Decrypt_QuandoDadosCorretos_DeveTerMesmoTamanhoOrigem()
    {
        var service = CriarService();
        var dados = new byte[] { 1, 2, 3, 4, 5 };

        var encrypted = service.Encrypt(dados).Value;
        var decrypted = service.Decrypt(encrypted).Value;

        decrypted.Length.ShouldBe(dados.Length);
    }
}
