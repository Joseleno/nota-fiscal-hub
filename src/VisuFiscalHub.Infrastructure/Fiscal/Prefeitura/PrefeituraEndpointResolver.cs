using VisuFiscalHub.Domain.Enums;

namespace VisuFiscalHub.Infrastructure.Fiscal.Prefeitura;

/// <summary>
/// Resolve URLs dos webservices das prefeituras por código IBGE do município.
/// Endpoints variam por fornecedor de sistema (Betha, IPM, Governa, NFS-e Nacional).
/// Em homologação, prefeituras sem mapeamento retornam null (não integradas).
/// </summary>
internal static class PrefeituraEndpointResolver
{
    private static readonly Dictionary<int, (string Homologacao, string Producao)> Endpoints = new()
    {
        // São Paulo (SP) — NFS-e São Paulo
        [3550308] = (
            "https://nfe.prefeitura.sp.gov.br/ws/lotenfe.asmx",
            "https://nfe.prefeitura.sp.gov.br/ws/lotenfe.asmx"),
    };

    public static string? ResolverGerarNfse(int codigoMunicipio, AmbienteSefaz ambiente)
    {
        if (!Endpoints.TryGetValue(codigoMunicipio, out var pair))
            return null;

        return ambiente == AmbienteSefaz.Producao ? pair.Producao : pair.Homologacao;
    }

    public static string? ResolverConsultarNfse(int codigoMunicipio, AmbienteSefaz ambiente) =>
        ResolverGerarNfse(codigoMunicipio, ambiente);
}
