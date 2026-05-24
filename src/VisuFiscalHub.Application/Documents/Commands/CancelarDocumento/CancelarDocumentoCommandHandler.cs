using Mediator;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Errors;
using VisuFiscalHub.Domain.Interfaces;

namespace VisuFiscalHub.Application.Documents.Commands.CancelarDocumento;

public sealed class CancelarDocumentoCommandHandler
    : ICommandHandler<CancelarDocumentoCommand, Result<CancelarDocumentoResponse>>
{
    private readonly IDocumentoFiscalRepository _documentoRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICancelamentoJobQueue _jobQueue;
    private readonly TimeProvider _timeProvider;

    public CancelarDocumentoCommandHandler(
        IDocumentoFiscalRepository documentoRepo,
        IUnitOfWork unitOfWork,
        ICancelamentoJobQueue jobQueue,
        TimeProvider timeProvider)
    {
        _documentoRepo = documentoRepo;
        _unitOfWork    = unitOfWork;
        _jobQueue      = jobQueue;
        _timeProvider  = timeProvider;
    }

    public async ValueTask<Result<CancelarDocumentoResponse>> Handle(
        CancelarDocumentoCommand command,
        CancellationToken cancellationToken)
    {
        var documento = await _documentoRepo.GetByIdForUpdateAsync(command.DocumentoId, cancellationToken);
        if (documento is null)
            return Result.Failure<CancelarDocumentoResponse>(DocumentoFiscalErrors.NaoEncontrado);

        if (documento.ClienteAppId != command.ClienteAppId)
            return Result.Failure<CancelarDocumentoResponse>(TenantErrors.NaoPertenceAoClienteApp);

        var iniciarResult = documento.IniciarCancelamento(_timeProvider);
        if (iniciarResult.IsFailure)
            return Result.Failure<CancelarDocumentoResponse>(iniciarResult.Error);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _jobQueue.EnqueueCancelamentoAsync(documento.Id, command.Justificativa, cancellationToken);

        return Result.Success(new CancelarDocumentoResponse(documento.Id, documento.Status));
    }
}
