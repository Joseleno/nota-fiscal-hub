using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Application.Common.Models;

public sealed record DocumentoStatusResponse(
    DocumentoFiscalId DocumentoId,
    StatusDocumento Status,
    string? ChaveAcesso,
    string? QrCode,
    string? Protocolo,
    string? MotivoRejeicao,
    DateTimeOffset? AuthorizedAt);
