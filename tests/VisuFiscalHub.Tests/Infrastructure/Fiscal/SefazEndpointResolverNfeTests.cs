using Shouldly;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Infrastructure.Fiscal.Sefaz;

namespace VisuFiscalHub.Tests.Infrastructure.Fiscal;

public class SefazEndpointResolverNfeTests
{
    // NF-e SVRS: AC(12), AL(27), AP(16), DF(53), ES(32), PB(25), RJ(33), RN(24), RO(11), RR(14), SC(42), SE(28), TO(17)
    // NF-e SVAN: AM(13), BA(29), CE(23), GO(52), MA(21), MS(50), MT(51), PA(15), PE(26), PI(22)
    // Own server: MG(31), SP(35), PR(41), RS(43) and others

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
    [InlineData(13)] // AM — SVAN
    [InlineData(29)] // BA — SVAN
    [InlineData(52)] // GO — SVAN
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
        // NFC-e AM uses its own server; NF-e AM uses SVAN
        var nfceUrl = SefazEndpointResolver.Autorizacao(13, AmbienteSefaz.Producao);
        var nfeUrl  = SefazEndpointResolver.AutorizacaoNfe(13, AmbienteSefaz.Producao);
        nfceUrl.ShouldContain("sefaz.am.gov.br");
        nfeUrl.ShouldContain("sefazvirtual");
    }

    [Fact]
    public void ResolveEventoNfe_Homologacao_RetornaUrlCorreta()
    {
        var url = SefazEndpointResolver.ResolveEventoNfe(35, AmbienteSefaz.Homologacao);
        url.ShouldContain("NFeRecepcaoEvento4");
        url.ShouldContain("homologacao");
    }
}
