using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Application.Common.Interfaces;

public sealed record SefazRetorno(
    bool Autorizado,
    string CStat,
    string XMotivo,
    string? NProt,
    string? XmlAutorizado,
    string? QrCodeUrl,
    long ElapsedMs);

public sealed record SefazConsultaRetorno(
    bool Encontrado,
    bool Autorizado,
    string CStat,
    string? NProt,
    string? XmlProtocolo,
    long ElapsedMs);

public interface ISefazClient
{
    Task<Result<SefazRetorno>> SubmeterAutorizacaoAsync(
        DocumentoFiscalId documentoId,
        TenantId tenantId,
        CancellationToken ct);

    Task<Result<SefazConsultaRetorno>> ConsultarNfeAsync(
        string chaveAcesso,
        TenantId tenantId,
        TipoDocumento tipo,
        CancellationToken ct);
}
