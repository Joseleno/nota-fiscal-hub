using Mediator;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Application.Documents.Queries.GetDocumentXml;

public sealed record GetDocumentXmlQuery : IQuery<Result<string>>
{
    public DocumentoFiscalId DocumentoId { get; init; }
    public ClienteAppId ClienteAppId { get; init; }
}
