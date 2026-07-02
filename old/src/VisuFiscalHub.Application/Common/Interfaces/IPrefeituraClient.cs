using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Application.Common.Interfaces;

public sealed record PrefeituraRetorno(
    bool Autorizado,
    string? NumeroNfse,
    string? Protocolo,
    string? XmlNfse,
    string? MotivoErro,
    long ElapsedMs);

public sealed record PrefeituraConsultaRetorno(
    bool Encontrado,
    bool Autorizado,
    string? NumeroNfse,
    string? XmlNfse,
    string? MotivoErro,
    long ElapsedMs);

public interface IPrefeituraClient
{
    Task<Result<PrefeituraRetorno>> EnviarRpsAsync(
        DocumentoFiscalId documentoId,
        TenantId tenantId,
        CancellationToken ct);

    Task<Result<PrefeituraConsultaRetorno>> ConsultarNfseAsync(
        string numeroRps,
        string serieRps,
        TenantId tenantId,
        int codigoMunicipio,
        CancellationToken ct);
}
