using Mediator;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Errors;
using VisuFiscalHub.Domain.Interfaces;

namespace VisuFiscalHub.Application.Documents.Queries.GetDocumentStatus;

internal sealed class GetDocumentStatusQueryHandler
    : IQueryHandler<GetDocumentStatusQuery, Result<DocumentoStatusResponse>>
{
    private readonly IDocumentoFiscalRepository _documentoRepo;
    private readonly ITenantRepository _tenantRepo;

    public GetDocumentStatusQueryHandler(
        IDocumentoFiscalRepository documentoRepo,
        ITenantRepository tenantRepo)
    {
        _documentoRepo = documentoRepo;
        _tenantRepo = tenantRepo;
    }

    public async ValueTask<Result<DocumentoStatusResponse>> Handle(
        GetDocumentStatusQuery query,
        CancellationToken cancellationToken)
    {
        var documento = await _documentoRepo.GetByIdAsync(query.DocumentoId, cancellationToken);
        if (documento is null)
            return Result.Failure<DocumentoStatusResponse>(DocumentoFiscalErrors.NaoEncontrado);

        // Validate that the document belongs to the requesting ClienteApp via Tenant
        var tenant = await _tenantRepo.GetByIdAsync(documento.TenantId, cancellationToken);
        if (tenant is null || tenant.ClienteAppId != query.ClienteAppId)
            return Result.Failure<DocumentoStatusResponse>(TenantErrors.NaoPertenceAoClienteApp);

        return Result.Success(new DocumentoStatusResponse(
            documento.Id,
            documento.Status,
            documento.ChaveAcesso?.Valor,
            documento.QrCode?.UrlCompleta,
            documento.Protocolo,
            documento.MotivoRejeicao,
            documento.AuthorizedAt));
    }
}
