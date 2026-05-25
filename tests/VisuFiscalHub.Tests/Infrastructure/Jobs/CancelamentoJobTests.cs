using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.Interfaces;
using VisuFiscalHub.Domain.ValueObjects;
using VisuFiscalHub.Infrastructure.Fiscal;
using VisuFiscalHub.Infrastructure.Fiscal.Sefaz;
using VisuFiscalHub.Infrastructure.Jobs;
using VisuFiscalHub.Infrastructure.Persistence;
using VisuFiscalHub.Tests.Helpers;

namespace VisuFiscalHub.Tests.Infrastructure.Jobs;

/// <summary>
/// Testa o fluxo de cancelamento de documentos fiscais via CancelamentoJob.
/// Usa DocumentoFiscal real para garantir que as transições de estado são exercitadas
/// com as invariantes do domínio, não apenas com mocks.
/// </summary>
public class CancelamentoJobTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly IDocumentoFiscalRepository _documentoRepo = Substitute.For<IDocumentoFiscalRepository>();
    private readonly ITenantRepository           _tenantRepo    = Substitute.For<ITenantRepository>();
    private readonly ITenantCertificateProvider  _certProvider  = Substitute.For<ITenantCertificateProvider>();
    private readonly IUnitOfWork                 _unitOfWork    = Substitute.For<IUnitOfWork>();
    private readonly TimeProvider                _timeProvider  = new FixedTimeProvider(FixedNow);

    private CancelamentoJob CreateJob()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var dbContext = new ApplicationDbContext(options, NullLoggerFactory.Instance);

        return new CancelamentoJob(
            _documentoRepo,
            _tenantRepo,
            _certProvider,
            new XmlSigner(),
            new SefazHttpClient(),
            dbContext,
            _unitOfWork,
            _timeProvider,
            NullLogger<CancelamentoJob>.Instance);
    }

    // ── documento não encontrado ──────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_DocumentoNaoEncontrado_NaoLancaExcecao()
    {
        var id = DocumentoFiscalId.New();
        _documentoRepo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((DocumentoFiscal?)null);

        await Should.NotThrowAsync(() => CreateJob().ExecuteAsync(id, "Erro de digitação", CancellationToken.None));

        await _tenantRepo.DidNotReceiveWithAnyArgs().GetByIdAsync(default!, default);
    }

    // ── idempotência: status diferente de Cancelando é ignorado ─────────────────

    [Theory]
    [InlineData(StatusDocumento.Autorizado)]
    [InlineData(StatusDocumento.Cancelado)]
    [InlineData(StatusDocumento.Falhou)]
    [InlineData(StatusDocumento.Criado)]
    public async Task ExecuteAsync_StatusNaoCancelando_NaoConsultaTenant(StatusDocumento status)
    {
        var id       = DocumentoFiscalId.New();
        var documento = DocumentoFiscalBuilder.EmStatus(status);
        _documentoRepo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(documento);

        await Should.NotThrowAsync(() => CreateJob().ExecuteAsync(id, "Erro de digitação", CancellationToken.None));

        await _tenantRepo.DidNotReceiveWithAnyArgs().GetByIdAsync(default!, default);
    }

    // ── documento sem Protocolo não chega ao tenant lookup ───────────────────────

    [Fact]
    public async Task ExecuteAsync_DocumentoSemProtocolo_NaoConsultaTenant()
    {
        var id       = DocumentoFiscalId.New();
        // Cancelando() cria um documento com Protocolo="PROT001" via builder.
        // Para simular sem protocolo, usamos reflexão para remover.
        var documento = DocumentoFiscalBuilder.Cancelando(FixedNow);
        var protocProp = typeof(DocumentoFiscal).GetProperty(
            nameof(DocumentoFiscal.Protocolo),
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!;
        protocProp.SetValue(documento, null);

        _documentoRepo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(documento);

        await Should.NotThrowAsync(() => CreateJob().ExecuteAsync(id, "Erro de digitação", CancellationToken.None));

        await _tenantRepo.DidNotReceiveWithAnyArgs().GetByIdAsync(default!, default);
    }

    // ── LogContext path: documento Cancelando com Protocolo chega ao tenant lookup ──
    //
    // Verifica que a execução passa pelos guards de early-return e alcança as linhas
    // LogContext.PushProperty("TenantId", ...) e LogContext.PushProperty("DocumentoId", ...).
    // Como LogContext é um concern estático do Serilog que não expõe assertions,
    // a confirmação é comportamental: quando tenant retorna null, o job lança
    // InvalidOperationException — o que só acontece APÓS as linhas de LogContext terem
    // sido executadas. Se qualquer guard anterior fizesse return, a exceção não seria lançada.

    [Fact]
    public async Task ExecuteAsync_DocumentoCancelando_RegistraLogComDocumentoId()
    {
        var id       = DocumentoFiscalId.New();
        var documento = DocumentoFiscalBuilder.Cancelando(FixedNow);
        _documentoRepo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(documento);

        // Tenant null → InvalidOperationException após as linhas de LogContext
        _tenantRepo.GetByIdAsync(documento.TenantId, Arg.Any<CancellationToken>())
            .Returns((Tenant?)null);

        // A exceção confirma que a execução passou pelos LogContext.PushProperty (linhas
        // que ficam entre o guard de protocolo e o tenant lookup), e não retornou cedo.
        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => CreateJob().ExecuteAsync(id, "Erro de digitação", CancellationToken.None));

        ex.Message.ShouldContain(documento.TenantId.Value.ToString());
    }
}
