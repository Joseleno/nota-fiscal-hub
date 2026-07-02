using Shouldly;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Infrastructure.Fiscal.Sefaz;

namespace VisuFiscalHub.Tests.Infrastructure.Fiscal;

public class SefazEndpointResolverNfeTests
{
    // NF-e SVRS (16 UFs): AC(12), AL(27), AP(16), CE(23), DF(53), ES(32), PA(15), PB(25), PI(22),
    //                      RJ(33), RN(24), RO(11), RR(14), SC(42), SE(28), TO(17)
    // NF-e SVAN (1 UF):   MA(21) only
    // Own server:          AM(13), BA(29), GO(52), MG(31), MS(50), MT(51), PE(26), PR(41), RS(43), SP(35)

    [Theory]
    [InlineData(12)] // AC — SVRS
    [InlineData(53)] // DF — SVRS
    [InlineData(32)] // ES — SVRS
    public void AutorizacaoNfe_UfSvrs_RetornaUrlSvrs(int uf)
    {
        var url = SefazEndpointResolver.AutorizacaoNfe(uf, AmbienteSefaz.Producao);
        url.ShouldContain("nfe.svrs.rs.gov.br");
        url.ShouldContain("NFeAutorizacao4");
    }

    [Theory]
    [InlineData(21)] // MA — only SVAN UF for NF-e
    public void AutorizacaoNfe_UfSvan_RetornaUrlSvan(int uf)
    {
        var url = SefazEndpointResolver.AutorizacaoNfe(uf, AmbienteSefaz.Producao);
        url.ShouldContain("www.sefazvirtual.fazenda.gov.br");
    }

    [Theory]
    [InlineData(35)] // SP — own server
    [InlineData(31)] // MG — own server
    [InlineData(41)] // PR — own server
    [InlineData(43)] // RS — own server
    [InlineData(13)] // AM — own server
    [InlineData(29)] // BA — own server
    [InlineData(52)] // GO — own server
    public void AutorizacaoNfe_UfPropria_RetornaUrlEsperada(int uf)
    {
        var url = SefazEndpointResolver.AutorizacaoNfe(uf, AmbienteSefaz.Producao);
        url.ShouldNotBeNullOrEmpty();
        url.ShouldContain("NFeAutorizacao4");
    }

    [Fact]
    public void AutorizacaoNfe_Homologacao_RetornaUrlHomologacao()
    {
        var url = SefazEndpointResolver.AutorizacaoNfe(35, AmbienteSefaz.Homologacao);
        url.ShouldContain("homologacao");
    }

    [Fact]
    public void NfceENfeCobremUfsDistintas()
    {
        // NFC-e AM uses sefaz.am.gov.br; NF-e AM also uses sefaz.am.gov.br (own server, not SVAN)
        var nfceUrl = SefazEndpointResolver.Autorizacao(13, AmbienteSefaz.Producao);
        var nfeUrl  = SefazEndpointResolver.AutorizacaoNfe(13, AmbienteSefaz.Producao);
        nfceUrl.ShouldContain("sefaz.am.gov.br");
        nfeUrl.ShouldContain("sefaz.am.gov.br");
        // The actual service paths differ (NfceAutorizacao vs NFeAutorizacao4)
        nfceUrl.ShouldNotBe(nfeUrl);
    }

    [Fact]
    public void ResolveEventoNfe_Homologacao_RetornaUrlCorreta()
    {
        var url = SefazEndpointResolver.ResolveEventoNfe(35, AmbienteSefaz.Homologacao);
        url.ShouldContain("NFeRecepcaoEvento4");
        url.ShouldContain("homologacao");
    }

    [Fact]
    public void ConsultaProtocoloNfe_UfSvrs_RetornaUrlSvrs()
    {
        var url = SefazEndpointResolver.ConsultaProtocoloNfe(12, AmbienteSefaz.Producao);
        url.ShouldContain("nfe.svrs.rs.gov.br");
        url.ShouldContain("NFeConsultaProtocolo4");
    }

    [Fact]
    public void ConsultaProtocoloNfe_UfPropria_RetornaUrl()
    {
        var url = SefazEndpointResolver.ConsultaProtocoloNfe(35, AmbienteSefaz.Producao);
        url.ShouldNotBeNullOrEmpty();
        url.ShouldContain("NFeConsultaProtocolo4");
    }
}
