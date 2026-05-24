using VisuFiscalHub.Domain.Enums;

namespace VisuFiscalHub.Infrastructure.Fiscal.Sefaz;

/// <summary>
/// Resolve os endpoints dos webservices SEFAZ para NFC-e por UF e ambiente.
/// Fonte: Portal SEFAZ — Ato COTEPE/ICMS 44/2018 e atualizações.
/// Autorizadora SVRS cobre UFs sem serviço próprio (AC, AL, AP, DF, ES, PB, PI, RJ, RN, RO, RR, SC, SE, TO).
/// Autorizadora SVC-AN utilizada como contingência nacional (não implementada nesta fase).
/// </summary>
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
}
