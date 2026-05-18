using Shouldly;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Infrastructure.Fiscal.Sefaz;

namespace VisuFiscalHub.Tests.Infrastructure.Fiscal.Sefaz;

/// <summary>
/// Verifica o mapeamento de UF → endpoint SEFAZ para os três webservices NFC-e.
/// Testa: (1) UFs com autorizadora própria; (2) UFs roteadas para SVRS; (3) UF inválida.
/// </summary>
public class SefazEndpointResolverTests
{
    // Todos os 27 códigos IBGE de UF com respectivo roteamento.
    // UFs com autorizadora própria → endpoint exclusivo.
    // UFs SVRS (14 UFs) → svrs.rs.gov.br
    private static readonly int[] UfsComAutorizadoraPropria =
    [
        13, // AM
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

    // ── Autorizacao ──────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(TodasAsUfs))]
    public void Autorizacao_UfValida_RetornaUrlNaoVazia(int ufCodigo, AmbienteSefaz ambiente)
    {
        var url = SefazEndpointResolver.Autorizacao(ufCodigo, ambiente);

        url.ShouldNotBeNullOrWhiteSpace();
        url.ShouldStartWith("https://");
    }

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
    [MemberData(nameof(UfsPropriaData))]
    public void Autorizacao_UfComAutorizadoraPropria_NaoRoteiaPorSvrs(int ufCodigo)
    {
        var producao = SefazEndpointResolver.Autorizacao(ufCodigo, AmbienteSefaz.Producao);
        var homologacao = SefazEndpointResolver.Autorizacao(ufCodigo, AmbienteSefaz.Homologacao);

        producao.ShouldNotContain("svrs.rs.gov.br");
        homologacao.ShouldNotContain("svrs.rs.gov.br");
        producao.ShouldNotBe(homologacao);
    }

    [Fact]
    public void Autorizacao_UfDesconhecida_LancaInvalidOperationException()
    {
        Should.Throw<InvalidOperationException>(
            () => SefazEndpointResolver.Autorizacao(99, AmbienteSefaz.Producao));
    }

    // ── ConsultaProtocolo ────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(TodasAsUfs))]
    public void ConsultaProtocolo_UfValida_RetornaUrlNaoVazia(int ufCodigo, AmbienteSefaz ambiente)
    {
        var url = SefazEndpointResolver.ConsultaProtocolo(ufCodigo, ambiente);

        url.ShouldNotBeNullOrWhiteSpace();
        url.ShouldStartWith("https://");
    }

    [Theory]
    [MemberData(nameof(UfsSvrsData))]
    public void ConsultaProtocolo_UfSvrs_RoteiaPorSvrs(int ufCodigo)
    {
        var url = SefazEndpointResolver.ConsultaProtocolo(ufCodigo, AmbienteSefaz.Producao);

        url.ShouldContain("svrs.rs.gov.br");
        url.ShouldContain("NFeConsultaProtocolo4");
    }

    [Theory]
    [MemberData(nameof(UfsPropriaData))]
    public void ConsultaProtocolo_UfComAutorizadoraPropria_NaoRoteiaPorSvrs(int ufCodigo)
    {
        var url = SefazEndpointResolver.ConsultaProtocolo(ufCodigo, AmbienteSefaz.Producao);

        url.ShouldNotContain("svrs.rs.gov.br");
    }

    [Fact]
    public void ConsultaProtocolo_UfDesconhecida_LancaInvalidOperationException()
    {
        Should.Throw<InvalidOperationException>(
            () => SefazEndpointResolver.ConsultaProtocolo(99, AmbienteSefaz.Producao));
    }

    // ── RetAutorizacao ───────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(TodasAsUfs))]
    public void RetAutorizacao_UfValida_RetornaUrlNaoVazia(int ufCodigo, AmbienteSefaz ambiente)
    {
        var url = SefazEndpointResolver.RetAutorizacao(ufCodigo, ambiente);

        url.ShouldNotBeNullOrWhiteSpace();
        url.ShouldStartWith("https://");
    }

    [Theory]
    [MemberData(nameof(UfsSvrsData))]
    public void RetAutorizacao_UfSvrs_RoteiaPorSvrs(int ufCodigo)
    {
        var url = SefazEndpointResolver.RetAutorizacao(ufCodigo, AmbienteSefaz.Producao);

        url.ShouldContain("svrs.rs.gov.br");
        url.ShouldContain("NFeRetAutorizacao4");
    }

    [Theory]
    [MemberData(nameof(UfsPropriaData))]
    public void RetAutorizacao_UfComAutorizadoraPropria_NaoRoteiaPorSvrs(int ufCodigo)
    {
        var url = SefazEndpointResolver.RetAutorizacao(ufCodigo, AmbienteSefaz.Producao);

        url.ShouldNotContain("svrs.rs.gov.br");
    }

    [Fact]
    public void RetAutorizacao_UfDesconhecida_LancaInvalidOperationException()
    {
        Should.Throw<InvalidOperationException>(
            () => SefazEndpointResolver.RetAutorizacao(99, AmbienteSefaz.Producao));
    }

    // ── ambientes diferentes produzem URLs distintas ─────────────────────────────

    [Theory]
    [MemberData(nameof(UfsPropriaData))]
    public void Autorizacao_ProducaoEHomologacao_UrlsDiferentes(int ufCodigo)
    {
        var prod = SefazEndpointResolver.Autorizacao(ufCodigo, AmbienteSefaz.Producao);
        var hom  = SefazEndpointResolver.Autorizacao(ufCodigo, AmbienteSefaz.Homologacao);

        prod.ShouldNotBe(hom);
    }

    // ── MemberData ───────────────────────────────────────────────────────────────

    public static IEnumerable<object[]> TodasAsUfs()
    {
        var allUfs = UfsComAutorizadoraPropria.Concat(UfsSvrs);
        foreach (var uf in allUfs)
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
