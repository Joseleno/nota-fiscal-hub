using VisuFiscalHub.Domain.Enums;

namespace VisuFiscalHub.Infrastructure.Fiscal.Sefaz;

/// <summary>
/// Resolve os endpoints dos webservices SEFAZ para NFC-e por UF e ambiente.
/// Fonte: Portal SEFAZ — Ato COTEPE/ICMS 44/2018 e atualizações.
/// Autorizadora SVRS cobre UFs sem serviço próprio (AC, AL, AP, DF, ES, PB, PI, RJ, RN, RO, RR, SC, SE, TO).
/// Autorizadora SVC-AN utilizada como contingência nacional (não implementada nesta fase).
/// </summary>
/// <remarks>
/// Este resolver é exclusivo para NF-e (Modelo 55) e NFC-e (Modelo 65).
/// NFS-e usa <see cref="VisuFiscalHub.Application.Common.Interfaces.IPrefeituraClient"/> — nunca deve chamar métodos deste resolver.
/// O guard é aplicado em FiscalDocumentProcessingJob antes de invocar este resolver.
/// </remarks>
internal static class SefazEndpointResolver
{
    private const string SvrsP = "https://nfce.svrs.rs.gov.br/ws";
    private const string SvrsH = "https://nfce-homologacao.svrs.rs.gov.br/ws";

    // Autorizadora SVRS — cobre UFs sem serviço próprio
    private static readonly IReadOnlySet<int> UfsSvrs = new HashSet<int>
    {
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
    };

    /// <summary>Retorna a URL do webservice de autorização NFC-e (NfeAutorizacao4).</summary>
    public static string Autorizacao(int ufCodigo, AmbienteSefaz ambiente)
    {
        var h = ambiente == AmbienteSefaz.Homologacao;

        if (UfsSvrs.Contains(ufCodigo))
        {
            var svrs = h ? SvrsH : SvrsP;
            return $"{svrs}/NFeAutorizacao4/NFeAutorizacao4.asmx";
        }

        return ufCodigo switch
        {
            13 => h ? "https://nfce-homologacao.sefaz.am.gov.br/services/NfeAutorizacao4"
                    : "https://nfce.sefaz.am.gov.br/services/NfeAutorizacao4",               // AM
            15 => h ? "https://appnfce.sefa.pa.gov.br:444/nfce-homologacao/NFeAutorizacao4"
                    : "https://appnfce.sefa.pa.gov.br:444/nfce/NFeAutorizacao4",              // PA
            21 => h ? "https://hom.sefaz.ma.gov.br/nfce/NFeAutorizacao4"
                    : "https://www.sefaz.ma.gov.br/nfce/NFeAutorizacao4",                     // MA
            23 => h ? "https://nfceh.sefaz.ce.gov.br/nfce/NFeAutorizacao4"
                    : "https://nfce.sefaz.ce.gov.br/nfce/NFeAutorizacao4",                    // CE
            26 => h ? "https://nfcehomolog.sefaz.pe.gov.br/nfce-server/services/NFeAutorizacao4"
                    : "https://nfce.sefaz.pe.gov.br/nfce-server/services/NFeAutorizacao4",    // PE
            29 => h ? "https://hnfe.sefaz.ba.gov.br/webservices/NFeAutorizacao4/NFeAutorizacao4.asmx"
                    : "https://nfe.sefaz.ba.gov.br/webservices/NFeAutorizacao4/NFeAutorizacao4.asmx", // BA
            31 => h ? "https://hnfce.fazenda.mg.gov.br/nfce/services/NFeAutorizacao4"
                    : "https://nfce.fazenda.mg.gov.br/nfce/services/NFeAutorizacao4",          // MG
            35 => h ? "https://homologacao.nfe.fazenda.sp.gov.br/nfceservice/services/NFeAutorizacao4"
                    : "https://nfe.fazenda.sp.gov.br/nfceservice/services/NFeAutorizacao4",    // SP
            41 => h ? "https://homologacao.nfce.pr.gov.br/nfce/NFeAutorizacao4"
                    : "https://nfce.pr.gov.br/nfce/NFeAutorizacao4",                           // PR
            43 => h ? "https://nfce-homologacao.sefazrs.rs.gov.br/ws/NfeAutorizacao/NFeAutorizacao4.asmx"
                    : "https://nfce.sefazrs.rs.gov.br/ws/NfeAutorizacao/NFeAutorizacao4.asmx", // RS
            50 => h ? "https://homologacao.nfce.fazenda.ms.gov.br/ws/NFeAutorizacao4"
                    : "https://nfce.fazenda.ms.gov.br/ws/NFeAutorizacao4",                     // MS
            51 => h ? "https://homologacao.sefaz.mt.gov.br/nfce/NFeAutorizacao4"
                    : "https://nfce.sefaz.mt.gov.br/nfce/NFeAutorizacao4",                     // MT
            52 => h ? "https://homologacao.nfce.sefaz.go.gov.br/nfce/NFeAutorizacao4"
                    : "https://nfce.sefaz.go.gov.br/nfce/NFeAutorizacao4",                     // GO
            _ => throw new InvalidOperationException($"UF {ufCodigo} sem endpoint de autorização mapeado.")
        };
    }

    /// <summary>
    /// Retorna a URL do webservice de consulta de situação da NF-e (NfeConsultaProtocolo4).
    /// Mapeamento explícito por UF — nunca derivado por string.Replace de outro endpoint.
    /// </summary>
    public static string ConsultaProtocolo(int ufCodigo, AmbienteSefaz ambiente)
    {
        var h = ambiente == AmbienteSefaz.Homologacao;

        if (UfsSvrs.Contains(ufCodigo))
        {
            var svrs = h ? SvrsH : SvrsP;
            return $"{svrs}/NFeConsultaProtocolo4/NFeConsultaProtocolo4.asmx";
        }

        return ufCodigo switch
        {
            13 => h ? "https://nfce-homologacao.sefaz.am.gov.br/services/NfeConsultaProtocolo4"
                    : "https://nfce.sefaz.am.gov.br/services/NfeConsultaProtocolo4",              // AM
            15 => h ? "https://appnfce.sefa.pa.gov.br:444/nfce-homologacao/NFeConsultaProtocolo4"
                    : "https://appnfce.sefa.pa.gov.br:444/nfce/NFeConsultaProtocolo4",            // PA
            21 => h ? "https://hom.sefaz.ma.gov.br/nfce/NFeConsultaProtocolo4"
                    : "https://www.sefaz.ma.gov.br/nfce/NFeConsultaProtocolo4",                   // MA
            23 => h ? "https://nfceh.sefaz.ce.gov.br/nfce/NFeConsultaProtocolo4"
                    : "https://nfce.sefaz.ce.gov.br/nfce/NFeConsultaProtocolo4",                  // CE
            26 => h ? "https://nfcehomolog.sefaz.pe.gov.br/nfce-server/services/NFeConsultaProtocolo4"
                    : "https://nfce.sefaz.pe.gov.br/nfce-server/services/NFeConsultaProtocolo4",  // PE
            29 => h ? "https://hnfe.sefaz.ba.gov.br/webservices/NFeConsultaProtocolo4/NFeConsultaProtocolo4.asmx"
                    : "https://nfe.sefaz.ba.gov.br/webservices/NFeConsultaProtocolo4/NFeConsultaProtocolo4.asmx", // BA
            31 => h ? "https://hnfce.fazenda.mg.gov.br/nfce/services/NFeConsultaProtocolo4"
                    : "https://nfce.fazenda.mg.gov.br/nfce/services/NFeConsultaProtocolo4",        // MG
            35 => h ? "https://homologacao.nfe.fazenda.sp.gov.br/nfceservice/services/NFeConsultaProtocolo4"
                    : "https://nfe.fazenda.sp.gov.br/nfceservice/services/NFeConsultaProtocolo4",  // SP
            41 => h ? "https://homologacao.nfce.pr.gov.br/nfce/NFeConsultaProtocolo4"
                    : "https://nfce.pr.gov.br/nfce/NFeConsultaProtocolo4",                         // PR
            43 => h ? "https://nfce-homologacao.sefazrs.rs.gov.br/ws/NfeConsultaProtocolo/NFeConsultaProtocolo4.asmx"
                    : "https://nfce.sefazrs.rs.gov.br/ws/NfeConsultaProtocolo/NFeConsultaProtocolo4.asmx", // RS
            50 => h ? "https://homologacao.nfce.fazenda.ms.gov.br/ws/NFeConsultaProtocolo4"
                    : "https://nfce.fazenda.ms.gov.br/ws/NFeConsultaProtocolo4",                   // MS
            51 => h ? "https://homologacao.sefaz.mt.gov.br/nfce/NFeConsultaProtocolo4"
                    : "https://nfce.sefaz.mt.gov.br/nfce/NFeConsultaProtocolo4",                   // MT
            52 => h ? "https://homologacao.nfce.sefaz.go.gov.br/nfce/NFeConsultaProtocolo4"
                    : "https://nfce.sefaz.go.gov.br/nfce/NFeConsultaProtocolo4",                   // GO
            _ => throw new InvalidOperationException($"UF {ufCodigo} sem endpoint de consulta mapeado.")
        };
    }

    /// <summary>Retorna a URL do webservice de retorno/consulta de autorização (NfeRetAutorizacao4).</summary>
    public static string RetAutorizacao(int ufCodigo, AmbienteSefaz ambiente)
    {
        var h = ambiente == AmbienteSefaz.Homologacao;

        if (UfsSvrs.Contains(ufCodigo))
        {
            var svrs = h ? SvrsH : SvrsP;
            return $"{svrs}/NFeRetAutorizacao4/NFeRetAutorizacao4.asmx";
        }

        return ufCodigo switch
        {
            13 => h ? "https://nfce-homologacao.sefaz.am.gov.br/services/NfeRetAutorizacao4"
                    : "https://nfce.sefaz.am.gov.br/services/NfeRetAutorizacao4",
            15 => h ? "https://appnfce.sefa.pa.gov.br:444/nfce-homologacao/NFeRetAutorizacao4"
                    : "https://appnfce.sefa.pa.gov.br:444/nfce/NFeRetAutorizacao4",
            21 => h ? "https://hom.sefaz.ma.gov.br/nfce/NFeRetAutorizacao4"
                    : "https://www.sefaz.ma.gov.br/nfce/NFeRetAutorizacao4",
            23 => h ? "https://nfceh.sefaz.ce.gov.br/nfce/NFeRetAutorizacao4"
                    : "https://nfce.sefaz.ce.gov.br/nfce/NFeRetAutorizacao4",
            26 => h ? "https://nfcehomolog.sefaz.pe.gov.br/nfce-server/services/NFeRetAutorizacao4"
                    : "https://nfce.sefaz.pe.gov.br/nfce-server/services/NFeRetAutorizacao4",
            29 => h ? "https://hnfe.sefaz.ba.gov.br/webservices/NFeRetAutorizacao4/NFeRetAutorizacao4.asmx"
                    : "https://nfe.sefaz.ba.gov.br/webservices/NFeRetAutorizacao4/NFeRetAutorizacao4.asmx",
            31 => h ? "https://hnfce.fazenda.mg.gov.br/nfce/services/NFeRetAutorizacao4"
                    : "https://nfce.fazenda.mg.gov.br/nfce/services/NFeRetAutorizacao4",
            35 => h ? "https://homologacao.nfe.fazenda.sp.gov.br/nfceservice/services/NFeRetAutorizacao4"
                    : "https://nfe.fazenda.sp.gov.br/nfceservice/services/NFeRetAutorizacao4",
            41 => h ? "https://homologacao.nfce.pr.gov.br/nfce/NFeRetAutorizacao4"
                    : "https://nfce.pr.gov.br/nfce/NFeRetAutorizacao4",
            43 => h ? "https://nfce-homologacao.sefazrs.rs.gov.br/ws/NfeRetAutorizacao/NFeRetAutorizacao4.asmx"
                    : "https://nfce.sefazrs.rs.gov.br/ws/NfeRetAutorizacao/NFeRetAutorizacao4.asmx",
            50 => h ? "https://homologacao.nfce.fazenda.ms.gov.br/ws/NFeRetAutorizacao4"
                    : "https://nfce.fazenda.ms.gov.br/ws/NFeRetAutorizacao4",
            51 => h ? "https://homologacao.sefaz.mt.gov.br/nfce/NFeRetAutorizacao4"
                    : "https://nfce.sefaz.mt.gov.br/nfce/NFeRetAutorizacao4",
            52 => h ? "https://homologacao.nfce.sefaz.go.gov.br/nfce/NFeRetAutorizacao4"
                    : "https://nfce.sefaz.go.gov.br/nfce/NFeRetAutorizacao4",
            _ => throw new InvalidOperationException($"UF {ufCodigo} sem endpoint de retorno autorização mapeado.")
        };
    }

    /// <summary>Retorna a URL do webservice de recepção de eventos (NFeRecepcaoEvento4).</summary>
    public static string ResolveEvento(int ufCodigo, AmbienteSefaz ambiente)
    {
        var h = ambiente == AmbienteSefaz.Homologacao;

        if (UfsSvrs.Contains(ufCodigo))
        {
            var svrs = h ? SvrsH : SvrsP;
            return $"{svrs}/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx";
        }

        return ufCodigo switch
        {
            13 => h ? "https://nfce-homologacao.sefaz.am.gov.br/services/NfeRecepcaoEvento4"
                    : "https://nfce.sefaz.am.gov.br/services/NfeRecepcaoEvento4",               // AM
            15 => h ? "https://appnfce.sefa.pa.gov.br:444/nfce-homologacao/NFeRecepcaoEvento4"
                    : "https://appnfce.sefa.pa.gov.br:444/nfce/NFeRecepcaoEvento4",              // PA
            21 => h ? "https://hom.sefaz.ma.gov.br/nfce/NFeRecepcaoEvento4"
                    : "https://www.sefaz.ma.gov.br/nfce/NFeRecepcaoEvento4",                     // MA
            23 => h ? "https://nfceh.sefaz.ce.gov.br/nfce/NFeRecepcaoEvento4"
                    : "https://nfce.sefaz.ce.gov.br/nfce/NFeRecepcaoEvento4",                    // CE
            26 => h ? "https://nfcehomolog.sefaz.pe.gov.br/nfce-server/services/NFeRecepcaoEvento4"
                    : "https://nfce.sefaz.pe.gov.br/nfce-server/services/NFeRecepcaoEvento4",    // PE
            29 => h ? "https://hnfe.sefaz.ba.gov.br/webservices/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx"
                    : "https://nfe.sefaz.ba.gov.br/webservices/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx", // BA
            31 => h ? "https://hnfce.fazenda.mg.gov.br/nfce/services/NFeRecepcaoEvento4"
                    : "https://nfce.fazenda.mg.gov.br/nfce/services/NFeRecepcaoEvento4",          // MG
            35 => h ? "https://homologacao.nfe.fazenda.sp.gov.br/nfceservice/services/NFeRecepcaoEvento4"
                    : "https://nfe.fazenda.sp.gov.br/nfceservice/services/NFeRecepcaoEvento4",    // SP
            41 => h ? "https://homologacao.nfce.pr.gov.br/nfce/NFeRecepcaoEvento4"
                    : "https://nfce.pr.gov.br/nfce/NFeRecepcaoEvento4",                           // PR
            43 => h ? "https://nfce-homologacao.sefazrs.rs.gov.br/ws/NfeRecepcaoEvento/NFeRecepcaoEvento4.asmx"
                    : "https://nfce.sefazrs.rs.gov.br/ws/NfeRecepcaoEvento/NFeRecepcaoEvento4.asmx", // RS
            50 => h ? "https://homologacao.nfce.fazenda.ms.gov.br/ws/NFeRecepcaoEvento4"
                    : "https://nfce.fazenda.ms.gov.br/ws/NFeRecepcaoEvento4",                     // MS
            51 => h ? "https://homologacao.sefaz.mt.gov.br/nfce/NFeRecepcaoEvento4"
                    : "https://nfce.sefaz.mt.gov.br/nfce/NFeRecepcaoEvento4",                     // MT
            52 => h ? "https://homologacao.nfce.sefaz.go.gov.br/nfce/NFeRecepcaoEvento4"
                    : "https://nfce.sefaz.go.gov.br/nfce/NFeRecepcaoEvento4",                     // GO
            _ => throw new InvalidOperationException($"UF {ufCodigo} não mapeada em ResolveEvento.")
        };
    }

    // ── NF-e Modelo 55 endpoints ────────────────────────────────────────────
    // SVRS: AC(12) AL(27) AP(16) CE(23) DF(53) ES(32) PA(15) PB(25) PI(22) RJ(33) RN(24) RO(11) RR(14) SC(42) SE(28) TO(17)
    private static readonly IReadOnlySet<int> UfsSvrsNfe = new HashSet<int>
    {
        12, // AC
        27, // AL
        16, // AP
        23, // CE
        53, // DF
        32, // ES
        15, // PA
        25, // PB
        22, // PI
        33, // RJ
        24, // RN
        11, // RO
        14, // RR
        42, // SC
        28, // SE
        17  // TO
    };

    // SVAN: MA(21) only
    private static readonly IReadOnlySet<int> UfsSvanNfe = new HashSet<int>
    {
        21  // MA
    };

    private const string SvrsNfeP = "https://nfe.svrs.rs.gov.br/ws";
    private const string SvrsNfeH = "https://nfe-homologacao.svrs.rs.gov.br/ws";

    public static string AutorizacaoNfe(int ufCodigo, AmbienteSefaz ambiente)
    {
        var h = ambiente == AmbienteSefaz.Homologacao;

        if (UfsSvrsNfe.Contains(ufCodigo))
            return $"{(h ? SvrsNfeH : SvrsNfeP)}/NFeAutorizacao4/NFeAutorizacao4.asmx";

        if (UfsSvanNfe.Contains(ufCodigo))
            return h
                ? "https://hom.sefazvirtual.fazenda.gov.br/NFeAutorizacao4/NFeAutorizacao4.asmx"
                : "https://www.sefazvirtual.fazenda.gov.br/NFeAutorizacao4/NFeAutorizacao4.asmx";

        return ufCodigo switch
        {
            13 => h ? "https://hom.sefaz.am.gov.br/services2/services/NfeAutorizacao4"
                    : "https://nfe.sefaz.am.gov.br/services2/services/NfeAutorizacao4",            // AM
            29 => h ? "https://hnfe.sefaz.ba.gov.br/webservices/NFeAutorizacao4/NFeAutorizacao4.asmx"
                    : "https://nfe.sefaz.ba.gov.br/webservices/NFeAutorizacao4/NFeAutorizacao4.asmx", // BA
            31 => h ? "https://hnfe.fazenda.mg.gov.br/nfe2/services/NFeAutorizacao4"
                    : "https://nfe.fazenda.mg.gov.br/nfe2/services/NFeAutorizacao4",               // MG
            35 => h ? "https://homologacao.nfe.fazenda.sp.gov.br/nfeservice/services/NFeAutorizacao4"
                    : "https://nfe.fazenda.sp.gov.br/nfeservice/services/NFeAutorizacao4",         // SP
            41 => h ? "https://homologacao.nfe.pr.gov.br/nfe/NFeAutorizacao4"
                    : "https://nfe.pr.gov.br/nfe/NFeAutorizacao4",                                 // PR
            43 => h ? "https://nfe-homologacao.sefazrs.rs.gov.br/ws/NfeAutorizacao/NFeAutorizacao4.asmx"
                    : "https://nfe.sefazrs.rs.gov.br/ws/NfeAutorizacao/NFeAutorizacao4.asmx",      // RS
            50 => h ? "https://hom.nfe.fazenda.ms.gov.br/ws/NFeAutorizacao4"
                    : "https://nfe.fazenda.ms.gov.br/ws/NFeAutorizacao4",                          // MS
            51 => h ? "https://homologacao.sefaz.mt.gov.br/services/NFeAutorizacao4"
                    : "https://nfe.sefaz.mt.gov.br/services/NFeAutorizacao4",                      // MT
            52 => h ? "https://hnfe.sefaz.go.gov.br/nfe/services/NFeAutorizacao4"
                    : "https://nfe.sefaz.go.gov.br/nfe/services/NFeAutorizacao4",                  // GO
            26 => h ? "https://nfeh.sefaz.pe.gov.br/nfe-service/services/NFeAutorizacao4"
                    : "https://nfe.sefaz.pe.gov.br/nfe-service/services/NFeAutorizacao4",          // PE
            _ => throw new InvalidOperationException($"UF {ufCodigo} sem endpoint NF-e de autorização mapeado.")
        };
    }

    public static string ConsultaProtocoloNfe(int ufCodigo, AmbienteSefaz ambiente)
    {
        var h = ambiente == AmbienteSefaz.Homologacao;

        if (UfsSvrsNfe.Contains(ufCodigo))
            return $"{(h ? SvrsNfeH : SvrsNfeP)}/NFeConsultaProtocolo4/NFeConsultaProtocolo4.asmx";

        if (UfsSvanNfe.Contains(ufCodigo))
            return h
                ? "https://hom.sefazvirtual.fazenda.gov.br/NFeConsultaProtocolo4/NFeConsultaProtocolo4.asmx"
                : "https://www.sefazvirtual.fazenda.gov.br/NFeConsultaProtocolo4/NFeConsultaProtocolo4.asmx";

        return ufCodigo switch
        {
            13 => h ? "https://hom.sefaz.am.gov.br/services2/services/NfeConsultaProtocolo4"
                    : "https://nfe.sefaz.am.gov.br/services2/services/NfeConsultaProtocolo4",            // AM
            29 => h ? "https://hnfe.sefaz.ba.gov.br/webservices/NFeConsultaProtocolo4/NFeConsultaProtocolo4.asmx"
                    : "https://nfe.sefaz.ba.gov.br/webservices/NFeConsultaProtocolo4/NFeConsultaProtocolo4.asmx", // BA
            31 => h ? "https://hnfe.fazenda.mg.gov.br/nfe2/services/NFeConsultaProtocolo4"
                    : "https://nfe.fazenda.mg.gov.br/nfe2/services/NFeConsultaProtocolo4",               // MG
            35 => h ? "https://homologacao.nfe.fazenda.sp.gov.br/nfeservice/services/NFeConsultaProtocolo4"
                    : "https://nfe.fazenda.sp.gov.br/nfeservice/services/NFeConsultaProtocolo4",         // SP
            41 => h ? "https://homologacao.nfe.pr.gov.br/nfe/NFeConsultaProtocolo4"
                    : "https://nfe.pr.gov.br/nfe/NFeConsultaProtocolo4",                                 // PR
            43 => h ? "https://nfe-homologacao.sefazrs.rs.gov.br/ws/NfeConsultaProtocolo/NFeConsultaProtocolo4.asmx"
                    : "https://nfe.sefazrs.rs.gov.br/ws/NfeConsultaProtocolo/NFeConsultaProtocolo4.asmx", // RS
            50 => h ? "https://hom.nfe.fazenda.ms.gov.br/ws/NFeConsultaProtocolo4"
                    : "https://nfe.fazenda.ms.gov.br/ws/NFeConsultaProtocolo4",                          // MS
            51 => h ? "https://homologacao.sefaz.mt.gov.br/services/NFeConsultaProtocolo4"
                    : "https://nfe.sefaz.mt.gov.br/services/NFeConsultaProtocolo4",                      // MT
            52 => h ? "https://hnfe.sefaz.go.gov.br/nfe/services/NFeConsultaProtocolo4"
                    : "https://nfe.sefaz.go.gov.br/nfe/services/NFeConsultaProtocolo4",                  // GO
            26 => h ? "https://nfeh.sefaz.pe.gov.br/nfe-service/services/NFeConsultaProtocolo4"
                    : "https://nfe.sefaz.pe.gov.br/nfe-service/services/NFeConsultaProtocolo4",          // PE
            _ => throw new InvalidOperationException($"UF {ufCodigo} sem endpoint NF-e de consulta mapeado.")
        };
    }

    public static string ResolveEventoNfe(int ufCodigo, AmbienteSefaz ambiente)
    {
        var h = ambiente == AmbienteSefaz.Homologacao;

        if (UfsSvrsNfe.Contains(ufCodigo))
            return $"{(h ? SvrsNfeH : SvrsNfeP)}/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx";

        if (UfsSvanNfe.Contains(ufCodigo))
            return h
                ? "https://hom.sefazvirtual.fazenda.gov.br/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx"
                : "https://www.sefazvirtual.fazenda.gov.br/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx";

        return ufCodigo switch
        {
            13 => h ? "https://hom.sefaz.am.gov.br/services2/services/NfeRecepcaoEvento4"
                    : "https://nfe.sefaz.am.gov.br/services2/services/NfeRecepcaoEvento4",            // AM
            29 => h ? "https://hnfe.sefaz.ba.gov.br/webservices/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx"
                    : "https://nfe.sefaz.ba.gov.br/webservices/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx", // BA
            31 => h ? "https://hnfe.fazenda.mg.gov.br/nfe2/services/NFeRecepcaoEvento4"
                    : "https://nfe.fazenda.mg.gov.br/nfe2/services/NFeRecepcaoEvento4",               // MG
            35 => h ? "https://homologacao.nfe.fazenda.sp.gov.br/nfeservice/services/NFeRecepcaoEvento4"
                    : "https://nfe.fazenda.sp.gov.br/nfeservice/services/NFeRecepcaoEvento4",         // SP
            41 => h ? "https://homologacao.nfe.pr.gov.br/nfe/NFeRecepcaoEvento4"
                    : "https://nfe.pr.gov.br/nfe/NFeRecepcaoEvento4",                                 // PR
            43 => h ? "https://nfe-homologacao.sefazrs.rs.gov.br/ws/NfeRecepcaoEvento/NFeRecepcaoEvento4.asmx"
                    : "https://nfe.sefazrs.rs.gov.br/ws/NfeRecepcaoEvento/NFeRecepcaoEvento4.asmx",   // RS
            50 => h ? "https://hom.nfe.fazenda.ms.gov.br/ws/NFeRecepcaoEvento4"
                    : "https://nfe.fazenda.ms.gov.br/ws/NFeRecepcaoEvento4",                          // MS
            51 => h ? "https://homologacao.sefaz.mt.gov.br/services/NFeRecepcaoEvento4"
                    : "https://nfe.sefaz.mt.gov.br/services/NFeRecepcaoEvento4",                      // MT
            52 => h ? "https://hnfe.sefaz.go.gov.br/nfe/services/NFeRecepcaoEvento4"
                    : "https://nfe.sefaz.go.gov.br/nfe/services/NFeRecepcaoEvento4",                  // GO
            26 => h ? "https://nfeh.sefaz.pe.gov.br/nfe-service/services/NFeRecepcaoEvento4"
                    : "https://nfe.sefaz.pe.gov.br/nfe-service/services/NFeRecepcaoEvento4",          // PE
            _ => throw new InvalidOperationException($"UF {ufCodigo} sem endpoint NF-e de evento mapeado.")
        };
    }
}
