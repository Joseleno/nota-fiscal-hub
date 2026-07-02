using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Application.Common.Models;

public sealed record CancelarDocumentoResponse(
    DocumentoFiscalId DocumentoId,
    StatusDocumento Status);
