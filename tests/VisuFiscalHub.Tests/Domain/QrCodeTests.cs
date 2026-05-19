using Shouldly;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.ValueObjects;

namespace VisuFiscalHub.Tests.Domain;

public class QrCodeTests
{
    private static ChaveAcesso ChaveMoc70()
        => ChaveAcesso.From("35090614200167140065125001000001800100000093").Value;

    [Fact]
    public void Gerar_NaoDeveConterCscNaUrl()
    {
        var csc = "0123456789";
        var chave = ChaveMoc70();
        var result = QrCode.Gerar(chave, AmbienteSefaz.Homologacao, csc, "https://nfce.sefaz.am.gov.br/api2/consultarnfce");

        result.IsSuccess.ShouldBeTrue();
        result.Value.UrlCompleta.ShouldNotContain(csc);
    }

    [Theory]
    [InlineData(
        "35090614200167140065125001000001800100000097",
        2,
        "0123456789",
        "d6c58eb4516bf353ad0bcc4831270f2f971e7122")]
    public void Gerar_DeveProduirUrlComHashCorreto(string chaveValor, int tpAmb, string csc, string hashEsperado)
    {
        var chave = ChaveAcesso.From(chaveValor).Value;
        var ambiente = (AmbienteSefaz)tpAmb;
        var result = QrCode.Gerar(chave, ambiente, csc, "https://nfce.sefaz.am.gov.br/api2/consultarnfce");

        result.IsSuccess.ShouldBeTrue();
        result.Value.UrlCompleta.ShouldContain(hashEsperado);
    }

    [Fact]
    public void Gerar_QuandoCscVazio_DeveRetornarErro()
    {
        var result = QrCode.Gerar(ChaveMoc70(), AmbienteSefaz.Homologacao, "", "https://exemplo.com");
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Gerar_QuandoUrlVazia_DeveRetornarErro()
    {
        var result = QrCode.Gerar(ChaveMoc70(), AmbienteSefaz.Homologacao, "abc123", "");
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Gerar_DeveConterChaveAcessoNaUrl()
    {
        var chave = ChaveMoc70();
        var result = QrCode.Gerar(chave, AmbienteSefaz.Homologacao, "csc123", "https://exemplo.com");

        result.IsSuccess.ShouldBeTrue();
        result.Value.UrlCompleta.ShouldContain(chave.Valor);
    }
}
