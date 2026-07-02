using Mediator;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Errors;
using VisuFiscalHub.Domain.Interfaces;

namespace VisuFiscalHub.Application.Documents.Queries.GetDocumentXml;

internal sealed class GetDocumentXmlQueryHandler
    : IQueryHandler<GetDocumentXmlQuery, Result<string>>
{
    private readonly IDocumentoFiscalRepository _documentoRepo;

    public GetDocumentXmlQueryHandler(IDocumentoFiscalRepository documentoRepo)
    {
        _documentoRepo = documentoRepo;
    }

    public async ValueTask<Result<string>> Handle(
        GetDocumentXmlQuery query,
        CancellationToken cancellationToken)
    {
        var documento = await _documentoRepo.GetByIdAsync(query.DocumentoId, cancellationToken);
        if (documento is null)
            return Result.Failure<string>(DocumentoFiscalErrors.NaoEncontrado);

        if (documento.ClienteAppId != query.ClienteAppId)
            return Result.Failure<string>(TenantErrors.NaoPertenceAoClienteApp);

        if (string.IsNullOrEmpty(documento.XmlAssinado))
            return Result.Failure<string>(DocumentoFiscalErrors.XmlIndisponivel);

        return Result.Success(documento.XmlAssinado);
    }
}
