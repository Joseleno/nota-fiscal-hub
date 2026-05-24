using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Application.Common.Interfaces;

public sealed record SefazRetorno(
    bool Autorizado,
    string CStat,
    string XMotivo,
    string? NProt,
    string? XmlAutorizado,
    string? QrCodeUrl);

public sealed record SefazConsultaRetorno(
    bool Encontrado,
    bool Autorizado,
    string CStat,
    string? NProt,
    string? XmlProtocolo);

public interface ISefazClient
{
    Task<Result<SefazRetorno>> SubmeterAutorizacaoAsync(
        DocumentoFiscalId documentoId,
        TenantId tenantId,
        CancellationToken ct);

    Task<Result<SefazConsultaRetorno>> ConsultarNfeAsync(
        string chaveAcesso,
        TenantId tenantId,
        CancellationToken ct);
}
