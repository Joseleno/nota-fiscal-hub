using Shouldly;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Infrastructure.Fiscal.Sefaz;

namespace VisuFiscalHub.Tests.Infrastructure.Fiscal.Sefaz;

/// <summary>
/// Verifica o mapeamento de UF → endpoint SEFAZ para os três webservices NFC-e.
/// Testa: (1) todas as 27 UFs retornam URLs válidas; (2) roteamento SVRS vs. autorizadora
/// própria; (3) URLs concretas de UFs com padrões distintos; (4) UF inválida lança exceção.
/// </summary>
public class SefazEndpointResolverTests
{
    // UFs com autorizadora própria (webservices estaduais dedicados).
    // AM (13) é tratado como autorizadora própria nesta implementação, diferentemente
    // de algumas versões antigas da spec que o roteavam via SVRS (ver DA-08 em decisions.md).
    private static readonly int[] UfsComAutorizadoraPropria =
    [
        13, // AM — autorizadora própria (nfce.sefaz.am.gov.br)
        15, // PA
        21, // MA
        23, // CE
        26, // PE
        29, // BA
        31, // MG
        35, // SP
        41, // PR
        43, // RS
        50, // MS
        51, // MT
        52  // GO
    ];

    // UFs roteadas para autorizadora SVRS (nfce.svrs.rs.gov.br / nfce-homologacao.svrs.rs.gov.br).
    private static readonly int[] UfsSvrs =
    [
        12, // AC
        27, // AL
        16, // AP
        53, // DF
        32, // ES
        25, // PB
        22, // PI
        33, // RJ
        24, // RN
        11, // RO
        14, // RR
        42, // SC
        28, // SE
        17  // TO
    ];

    // ── invariante: 27 UFs cobertas ──────────────────────────────────────────────

    [Fact]
    public void UfsCobertos_TotalDeveSer27()
    {
        (UfsComAutorizadoraPropria.Length + UfsSvrs.Length).ShouldBe(27);
    }

    // ── todas as UFs: URL não vazia e HTTPS ───────────────────────────────────────

    [Theory]
    [MemberData(nameof(TodasAsUfs))]
    public void Autorizacao_UfValida_RetornaUrlHttpsNaoVazia(int ufCodigo, AmbienteSefaz ambiente)
    {
        var url = SefazEndpointResolver.Autorizacao(ufCodigo, ambiente);

        url.ShouldNotBeNullOrWhiteSpace();
        url.ShouldStartWith("https://");
    }

    [Theory]
    [MemberData(nameof(TodasAsUfs))]
    public void ConsultaProtocolo_UfValida_RetornaUrlHttpsNaoVazia(int ufCodigo, AmbienteSefaz ambiente)
    {
        var url = SefazEndpointResolver.ConsultaProtocolo(ufCodigo, ambiente);

        url.ShouldNotBeNullOrWhiteSpace();
        url.ShouldStartWith("https://");
    }

    [Theory]
    [MemberData(nameof(TodasAsUfs))]
    public void RetAutorizacao_UfValida_RetornaUrlHttpsNaoVazia(int ufCodigo, AmbienteSefaz ambiente)
    {
        var url = SefazEndpointResolver.RetAutorizacao(ufCodigo, ambiente);

        url.ShouldNotBeNullOrWhiteSpace();
        url.ShouldStartWith("https://");
    }

    // ── UFs SVRS: roteamento para svrs.rs.gov.br ─────────────────────────────────

    [Theory]
    [MemberData(nameof(UfsSvrsData))]
    public void Autorizacao_UfSvrsProducao_RoteiaPorSvrs(int ufCodigo)
    {
        var url = SefazEndpointResolver.Autorizacao(ufCodigo, AmbienteSefaz.Producao);

        url.ShouldContain("nfce.svrs.rs.gov.br");
        url.ShouldNotContain("homologacao");
    }

    [Theory]
    [MemberData(nameof(UfsSvrsData))]
    public void Autorizacao_UfSvrsHomologacao_RoteiaPorSvrsHomologacao(int ufCodigo)
    {
        var url = SefazEndpointResolver.Autorizacao(ufCodigo, AmbienteSefaz.Homologacao);

        url.ShouldContain("nfce-homologacao.svrs.rs.gov.br");
    }

    [Theory]
    [MemberData(nameof(UfsSvrsData))]
    public void ConsultaProtocolo_UfSvrs_ContemEndpointConsultaProtocolo(int ufCodigo)
    {
        var url = SefazEndpointResolver.ConsultaProtocolo(ufCodigo, AmbienteSefaz.Producao);

        url.ShouldContain("svrs.rs.gov.br");
        url.ShouldContain("NFeConsultaProtocolo4");
    }

    [Theory]
    [MemberData(nameof(UfsSvrsData))]
    public void RetAutorizacao_UfSvrs_ContemEndpointRetAutorizacao(int ufCodigo)
    {
        var url = SefazEndpointResolver.RetAutorizacao(ufCodigo, AmbienteSefaz.Producao);

        url.ShouldContain("svrs.rs.gov.br");
        url.ShouldContain("NFeRetAutorizacao4");
    }

    // ── UFs com autorizadora própria: não roteiam por SVRS ───────────────────────

    [Theory]
    [MemberData(nameof(UfsPropriaData))]
    public void Autorizacao_UfComAutorizadoraPropria_NaoRoteiaPorSvrs(int ufCodigo)
    {
        SefazEndpointResolver.Autorizacao(ufCodigo, AmbienteSefaz.Producao)
            .ShouldNotContain("svrs.rs.gov.br");
        SefazEndpointResolver.Autorizacao(ufCodigo, AmbienteSefaz.Homologacao)
            .ShouldNotContain("svrs.rs.gov.br");
    }

    [Theory]
    [MemberData(nameof(UfsPropriaData))]
    public void ConsultaProtocolo_UfComAutorizadoraPropria_NaoRoteiaPorSvrs(int ufCodigo)
    {
        SefazEndpointResolver.ConsultaProtocolo(ufCodigo, AmbienteSefaz.Producao)
            .ShouldNotContain("svrs.rs.gov.br");
    }

    [Theory]
    [MemberData(nameof(UfsPropriaData))]
    public void RetAutorizacao_UfComAutorizadoraPropria_NaoRoteiaPorSvrs(int ufCodigo)
    {
        SefazEndpointResolver.RetAutorizacao(ufCodigo, AmbienteSefaz.Producao)
            .ShouldNotContain("svrs.rs.gov.br");
    }

    // ── ambientes distintos produzem URLs distintas ───────────────────────────────

    [Theory]
    [MemberData(nameof(UfsPropriaData))]
    public void Autorizacao_ProducaoEHomologacao_UrlsDiferentes(int ufCodigo)
    {
        SefazEndpointResolver.Autorizacao(ufCodigo, AmbienteSefaz.Producao)
            .ShouldNotBe(SefazEndpointResolver.Autorizacao(ufCodigo, AmbienteSefaz.Homologacao));
    }

    [Theory]
    [MemberData(nameof(UfsPropriaData))]
    public void ConsultaProtocolo_ProducaoEHomologacao_UrlsDiferentes(int ufCodigo)
    {
        SefazEndpointResolver.ConsultaProtocolo(ufCodigo, AmbienteSefaz.Producao)
            .ShouldNotBe(SefazEndpointResolver.ConsultaProtocolo(ufCodigo, AmbienteSefaz.Homologacao));
    }

    [Theory]
    [MemberData(nameof(UfsPropriaData))]
    public void RetAutorizacao_ProducaoEHomologacao_UrlsDiferentes(int ufCodigo)
    {
        SefazEndpointResolver.RetAutorizacao(ufCodigo, AmbienteSefaz.Producao)
            .ShouldNotBe(SefazEndpointResolver.RetAutorizacao(ufCodigo, AmbienteSefaz.Homologacao));
    }

    // ── URLs concretas de UFs com padrões distintos ───────────────────────────────
    // Previne regressão do bug cdf1097 (RS mapeado para URL de SE antes da correção).
    // Valida o domínio completo das três UFs com padrões mais distintos: RS, SP e BA.

    [Theory]
    [InlineData(43, AmbienteSefaz.Producao,    "nfce.sefazrs.rs.gov.br")]
    [InlineData(43, AmbienteSefaz.Homologacao, "nfce-homologacao.sefazrs.rs.gov.br")]
    [InlineData(35, AmbienteSefaz.Producao,    "nfe.fazenda.sp.gov.br")]
    [InlineData(35, AmbienteSefaz.Homologacao, "homologacao.nfe.fazenda.sp.gov.br")]
    [InlineData(29, AmbienteSefaz.Producao,    "nfe.sefaz.ba.gov.br")]
    [InlineData(29, AmbienteSefaz.Homologacao, "hnfe.sefaz.ba.gov.br")]
    public void Autorizacao_UrlConcreta_ContemDominioEsperado(
        int ufCodigo, AmbienteSefaz ambiente, string dominioEsperado)
    {
        SefazEndpointResolver.Autorizacao(ufCodigo, ambiente)
            .ShouldContain(dominioEsperado);
    }

    [Theory]
    [InlineData(43, AmbienteSefaz.Producao,    "nfce.sefazrs.rs.gov.br")]
    [InlineData(43, AmbienteSefaz.Homologacao, "nfce-homologacao.sefazrs.rs.gov.br")]
    [InlineData(35, AmbienteSefaz.Producao,    "nfe.fazenda.sp.gov.br")]
    [InlineData(35, AmbienteSefaz.Homologacao, "homologacao.nfe.fazenda.sp.gov.br")]
    [InlineData(29, AmbienteSefaz.Producao,    "nfe.sefaz.ba.gov.br")]
    [InlineData(29, AmbienteSefaz.Homologacao, "hnfe.sefaz.ba.gov.br")]
    public void ConsultaProtocolo_UrlConcreta_ContemDominioEsperado(
        int ufCodigo, AmbienteSefaz ambiente, string dominioEsperado)
    {
        SefazEndpointResolver.ConsultaProtocolo(ufCodigo, ambiente)
            .ShouldContain(dominioEsperado);
    }

    [Theory]
    [InlineData(43, AmbienteSefaz.Producao,    "nfce.sefazrs.rs.gov.br")]
    [InlineData(43, AmbienteSefaz.Homologacao, "nfce-homologacao.sefazrs.rs.gov.br")]
    [InlineData(35, AmbienteSefaz.Producao,    "nfe.fazenda.sp.gov.br")]
    [InlineData(35, AmbienteSefaz.Homologacao, "homologacao.nfe.fazenda.sp.gov.br")]
    [InlineData(29, AmbienteSefaz.Producao,    "nfe.sefaz.ba.gov.br")]
    [InlineData(29, AmbienteSefaz.Homologacao, "hnfe.sefaz.ba.gov.br")]
    public void RetAutorizacao_UrlConcreta_ContemDominioEsperado(
        int ufCodigo, AmbienteSefaz ambiente, string dominioEsperado)
    {
        SefazEndpointResolver.RetAutorizacao(ufCodigo, ambiente)
            .ShouldContain(dominioEsperado);
    }

    // ── UF desconhecida → InvalidOperationException ───────────────────────────────

    [Fact]
    public void Autorizacao_UfDesconhecida_LancaInvalidOperationException()
    {
        Should.Throw<InvalidOperationException>(
            () => SefazEndpointResolver.Autorizacao(99, AmbienteSefaz.Producao));
    }

    [Fact]
    public void ConsultaProtocolo_UfDesconhecida_LancaInvalidOperationException()
    {
        Should.Throw<InvalidOperationException>(
            () => SefazEndpointResolver.ConsultaProtocolo(99, AmbienteSefaz.Producao));
    }

    [Fact]
    public void RetAutorizacao_UfDesconhecida_LancaInvalidOperationException()
    {
        Should.Throw<InvalidOperationException>(
            () => SefazEndpointResolver.RetAutorizacao(99, AmbienteSefaz.Producao));
    }

    // ── MemberData ───────────────────────────────────────────────────────────────

    public static IEnumerable<object[]> TodasAsUfs()
    {
        foreach (var uf in UfsComAutorizadoraPropria.Concat(UfsSvrs))
        {
            yield return [uf, AmbienteSefaz.Producao];
            yield return [uf, AmbienteSefaz.Homologacao];
        }
    }

    public static IEnumerable<object[]> UfsSvrsData()
        => UfsSvrs.Select(uf => new object[] { uf });

    public static IEnumerable<object[]> UfsPropriaData()
        => UfsComAutorizadoraPropria.Select(uf => new object[] { uf });
}
