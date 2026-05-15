using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Application.Common.Models;

public sealed record IssueDocumentResponse(
    DocumentoFiscalId DocumentoId,
    StatusDocumento Status,
    string ChaveAcesso,
    string PollUrl,
    DateTimeOffset CreatedAt);
