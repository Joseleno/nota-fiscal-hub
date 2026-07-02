using Mediator;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Application.Documents.Queries.GetDocumentStatus;

public sealed record GetDocumentStatusQuery : IQuery<Result<DocumentoStatusResponse>>
{
    public DocumentoFiscalId DocumentoId { get; init; }
    public ClienteAppId ClienteAppId { get; init; }
}
