using NSubstitute;
using Shouldly;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Application.Documents.Commands.CancelarDocumento;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.Interfaces;
using VisuFiscalHub.Tests.Helpers;

namespace VisuFiscalHub.Tests.Application;

public class CancelarDocumentoCommandHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    private static (CancelarDocumentoCommandHandler handler,
                    IDocumentoFiscalRepository docRepo,
                    IUnitOfWork unitOfWork,
                    ICancelamentoJobQueue jobQueue)
        CriarHandler(FixedTimeProvider timeProvider)
    {
        var docRepo    = Substitute.For<IDocumentoFiscalRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var jobQueue   = Substitute.For<ICancelamentoJobQueue>();
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(1));
        jobQueue.EnqueueCancelamentoAsync(
            Arg.Any<DocumentoFiscalId>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var handler = new CancelarDocumentoCommandHandler(docRepo, unitOfWork, jobQueue, timeProvider);
        return (handler, docRepo, unitOfWork, jobQueue);
    }

    [Fact]
    public async Task Handle_QuandoDocumentoAutorizado_EnfileirarJobERetornarCancelando()
    {
        var authorizedAt = FixedNow.AddMinutes(-10);
        var documento = DocumentoFiscalBuilder.Autorizado(authorizedAt);
        var (handler, docRepo, unitOfWork, jobQueue) = CriarHandler(new FixedTimeProvider(FixedNow));
        docRepo.GetByIdAsync(documento.Id, Arg.Any<CancellationToken>()).Returns(documento);
        var command = new CancelarDocumentoCommand
        {
            DocumentoId   = documento.Id,
            ClienteAppId  = documento.ClienteAppId,
            Justificativa = "Justificativa de cancelamento de teste aqui"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(StatusDocumento.Cancelando);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await jobQueue.Received(1).EnqueueCancelamentoAsync(
            documento.Id, command.Justificativa, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_QuandoDocumentoDeOutroClienteApp_RetornarErro403()
    {
        var documento = DocumentoFiscalBuilder.Autorizado(FixedNow.AddMinutes(-10));
        var (handler, docRepo, _, _) = CriarHandler(new FixedTimeProvider(FixedNow));
        docRepo.GetByIdAsync(documento.Id, Arg.Any<CancellationToken>()).Returns(documento);
        var command = new CancelarDocumentoCommand
        {
            DocumentoId   = documento.Id,
            ClienteAppId  = ClienteAppId.New(), // diferente do documento
            Justificativa = "Justificativa de cancelamento de teste aqui"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Tenant.NaoPertenceAoClienteApp");
    }

    [Fact]
    public async Task Handle_QuandoPrazoExpirado_RetornarErroPrazoDeCancelamentoExpirado()
    {
        var authorizedAt = FixedNow.AddMinutes(-31);
        var documento = DocumentoFiscalBuilder.Autorizado(authorizedAt);
        var (handler, docRepo, _, _) = CriarHandler(new FixedTimeProvider(FixedNow));
        docRepo.GetByIdAsync(documento.Id, Arg.Any<CancellationToken>()).Returns(documento);
        var command = new CancelarDocumentoCommand
        {
            DocumentoId   = documento.Id,
            ClienteAppId  = documento.ClienteAppId,
            Justificativa = "Justificativa de cancelamento de teste aqui"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DocumentoFiscal.PrazoDeCancelamentoExpirado");
    }

    [Fact]
    public async Task Handle_QuandoDocumentoNaoEncontrado_RetornarErro404()
    {
        var (handler, docRepo, _, _) = CriarHandler(new FixedTimeProvider(FixedNow));
        docRepo.GetByIdAsync(Arg.Any<DocumentoFiscalId>(), Arg.Any<CancellationToken>())
               .Returns((DocumentoFiscal?)null);
        var command = new CancelarDocumentoCommand
        {
            DocumentoId   = DocumentoFiscalId.New(),
            ClienteAppId  = ClienteAppId.New(),
            Justificativa = "Justificativa de cancelamento de teste aqui"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DocumentoFiscal.NaoEncontrado");
    }
}
