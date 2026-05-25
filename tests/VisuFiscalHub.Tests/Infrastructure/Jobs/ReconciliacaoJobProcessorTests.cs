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
using VisuFiscalHub.Infrastructure.Jobs;
using VisuFiscalHub.Tests.Helpers;

namespace VisuFiscalHub.Tests.Infrastructure.Jobs;

/// <summary>
/// Testa o fluxo de reconciliação de documentos travados em status Processando.
/// Usa DocumentoFiscal real para garantir que as transições de estado são exercitadas
/// com as invariantes do domínio.
/// </summary>
public class ReconciliacaoJobProcessorTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly IDocumentoFiscalRepository _documentoRepo = Substitute.For<IDocumentoFiscalRepository>();
    private readonly ISefazClient _sefazClient = Substitute.For<ISefazClient>();
    private readonly IDocumentJobQueue _documentJobQueue = Substitute.For<IDocumentJobQueue>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly TimeProvider _timeProvider = new FixedTimeProvider(FixedNow);

    private ReconciliacaoJobProcessor CreateProcessor() =>
        new(_documentoRepo, _sefazClient, _documentJobQueue, _unitOfWork, _timeProvider,
            NullLogger<ReconciliacaoJobProcessor>.Instance);

    // ── sem documentos travados ───────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SemDocumentosTravados_NaoConsultaSefaz()
    {
        _documentoRepo
            .GetProcessandoAntigoAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<DocumentoFiscal>());

        await CreateProcessor().ExecuteAsync(CancellationToken.None);

        await _sefazClient.DidNotReceiveWithAnyArgs()
            .ConsultarNfeAsync(default!, default, default, default);
    }

    // ── threshold passado ao repositório ─────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_PassaThresholdCorreto_10MinutosAtras()
    {
        var expectedThreshold = FixedNow.AddMinutes(-10);
        _documentoRepo
            .GetProcessandoAntigoAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<DocumentoFiscal>());

        await CreateProcessor().ExecuteAsync(CancellationToken.None);

        await _documentoRepo.Received(1).GetProcessandoAntigoAsync(
            Arg.Is<DateTimeOffset>(t => t == expectedThreshold),
            Arg.Any<CancellationToken>());
    }

    // ── consulta SEFAZ falhou → reenfileira, não salva ───────────────────────────

    [Fact]
    public async Task ExecuteAsync_ConsultaSefazFalhou_ReenfileiraProcessingJob()
    {
        var documento = DocumentoFiscalBuilder.Processando();
        ConfigurarDocumentosTravados(documento);

        _sefazClient
            .ConsultarNfeAsync(documento.ChaveAcesso.Valor, documento.TenantId, Arg.Any<TipoDocumento>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<SefazConsultaRetorno>(
                new Error("Sefaz.Timeout", "Timeout na consulta.")));

        await CreateProcessor().ExecuteAsync(CancellationToken.None);

        await _documentJobQueue.Received(1)
            .EnqueueProcessingAsync(documento.Id, Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── SEFAZ autorizado → Autorizado ────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SefazAutorizado_TransicionaParaAutorizado()
    {
        var documento = DocumentoFiscalBuilder.Processando();
        ConfigurarDocumentosTravados(documento);
        ConfigurarConsultaAutorizado(documento, protocolo: "315260000000001");

        await CreateProcessor().ExecuteAsync(CancellationToken.None);

        documento.Status.ShouldBe(StatusDocumento.Autorizado);
        documento.Protocolo.ShouldBe("315260000000001");
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── QrCode persistido é reutilizado na reconciliação ─────────────────────────

    [Fact]
    public async Task ExecuteAsync_SefazAutorizado_QrCodePersistidoPreservado()
    {
        const string persistedQrUrl = "https://www.sefaz.rs.gov.br/NFCE/NFCE-consulta.aspx?p=35260112345678000195650010000000011234567810|2|1|abc123";
        var documento = DocumentoFiscalBuilder.Processando();

        // Injetar QrCode persistido via reflexão (simula documento que foi emitido com QrCode)
        var qrProp = typeof(DocumentoFiscal).GetProperty(
            nameof(DocumentoFiscal.QrCode),
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!;
        qrProp.SetValue(documento, QrCode.FromStorage(persistedQrUrl));

        ConfigurarDocumentosTravados(documento);
        ConfigurarConsultaAutorizado(documento, protocolo: "315260000000001");

        await CreateProcessor().ExecuteAsync(CancellationToken.None);

        documento.Status.ShouldBe(StatusDocumento.Autorizado);
        documento.QrCode.ShouldNotBeNull();
        documento.QrCode!.UrlCompleta.ShouldBe(persistedQrUrl);
    }

    [Fact]
    public async Task ExecuteAsync_SefazAutorizado_SemQrCodePersistido_UsaChaveAcessoComoFallback()
    {
        var documento = DocumentoFiscalBuilder.Processando();
        // documento.QrCode é null por padrão no builder
        ConfigurarDocumentosTravados(documento);
        ConfigurarConsultaAutorizado(documento, protocolo: "315260000000002");

        await CreateProcessor().ExecuteAsync(CancellationToken.None);

        documento.Status.ShouldBe(StatusDocumento.Autorizado);
        documento.QrCode.ShouldNotBeNull("fallback deve preencher QrCode com chave de acesso");
        documento.QrCode!.UrlCompleta.ShouldBe(documento.ChaveAcesso.Valor);
    }

    // ── SEFAZ autorizado mas NProt ausente → Falhou ───────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SefazAutorizadoSemNProt_TransicionaParaFalhou()
    {
        var documento = DocumentoFiscalBuilder.Processando();
        ConfigurarDocumentosTravados(documento);

        _sefazClient
            .ConsultarNfeAsync(documento.ChaveAcesso.Valor, documento.TenantId, Arg.Any<TipoDocumento>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new SefazConsultaRetorno(
                Encontrado:   true,
                Autorizado:   true,
                CStat:        "100",
                NProt:        null,
                XmlProtocolo: null,
                ElapsedMs:    0L)));

        await CreateProcessor().ExecuteAsync(CancellationToken.None);

        documento.Status.ShouldBe(StatusDocumento.Falhou);
        // FalharAsync salva após transição bem-sucedida.
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── documento não encontrado na SEFAZ → Falhou ────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_NaoEncontradoNaSefaz_TransicionaParaFalhou()
    {
        var documento = DocumentoFiscalBuilder.Processando();
        ConfigurarDocumentosTravados(documento);
        ConfigurarConsultaNaoEncontrado(documento);

        await CreateProcessor().ExecuteAsync(CancellationToken.None);

        documento.Status.ShouldBe(StatusDocumento.Falhou);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── encontrado mas não autorizado (rejeição definitiva) → Falhou ─────────────

    [Fact]
    public async Task ExecuteAsync_EncontradoNaoAutorizado_TransicionaParaFalhou()
    {
        var documento = DocumentoFiscalBuilder.Processando();
        ConfigurarDocumentosTravados(documento);

        _sefazClient
            .ConsultarNfeAsync(documento.ChaveAcesso.Valor, documento.TenantId, Arg.Any<TipoDocumento>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new SefazConsultaRetorno(
                Encontrado:   true,
                Autorizado:   false,
                CStat:        "110",
                NProt:        null,
                XmlProtocolo: null,
                ElapsedMs:    0L)));

        await CreateProcessor().ExecuteAsync(CancellationToken.None);

        documento.Status.ShouldBe(StatusDocumento.Falhou);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── Autorizar() retorna falha → não salva, não propaga exceção ───────────────
    // Testa o ramo AuthResult.IsFailure em AutorizarPorConsultaAsync: o documento não
    // está em Processando quando o método Autorizar() é invocado, então a transição falha.

    [Fact]
    public async Task ExecuteAsync_AutorizarRetornaFailure_NaoSalvaENaoPropagaExcecao()
    {
        var documento = DocumentoFiscalBuilder.Processando();
        ConfigurarDocumentosTravados(documento);

        // Força status para fora de Processando antes da reconciliação: Autorizar() vai falhar.
        ForcarStatus(documento, StatusDocumento.Autorizado);

        ConfigurarConsultaAutorizado(documento, protocolo: "315260000000001");

        await Should.NotThrowAsync(() => CreateProcessor().ExecuteAsync(CancellationToken.None));

        // authResult.IsFailure → retorna silenciosamente, sem salvar.
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── transição concorrente em Falhar() → LogInformation, não propaga exceção ──
    // Representa o cenário onde NfceProcessingJob transitou o documento para um status
    // final entre GetProcessandoAntigoAsync e a chamada FalharAsync na reconciliação.

    [Fact]
    public async Task ExecuteAsync_FalharRetornaFailure_NaoPropagaExcecao()
    {
        var documento = DocumentoFiscalBuilder.Processando();
        ConfigurarDocumentosTravados(documento);

        // Simula que NfceProcessingJob transitou concorrentemente para Autorizado.
        ForcarStatus(documento, StatusDocumento.Autorizado);

        ConfigurarConsultaNaoEncontrado(documento);

        // Falhar() retorna Failure (status != Processando) — deve logar Info, não lançar.
        await Should.NotThrowAsync(() => CreateProcessor().ExecuteAsync(CancellationToken.None));

        // Nenhuma transição ocorreu — não deve salvar.
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── múltiplos documentos: todos reconciliados, todos salvos ──────────────────

    [Fact]
    public async Task ExecuteAsync_DoisDocumentosTravados_AmbosReconciliadosESalvos()
    {
        // Números distintos para que as chaves de acesso sejam diferentes.
        var doc1 = DocumentoFiscalBuilder.Processando(numero: 1);
        var doc2 = DocumentoFiscalBuilder.Processando(numero: 2);

        _documentoRepo
            .GetProcessandoAntigoAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new[] { doc1, doc2 });

        _sefazClient
            .ConsultarNfeAsync(Arg.Any<string>(), Arg.Any<TenantId>(), Arg.Any<TipoDocumento>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new SefazConsultaRetorno(
                Encontrado:   false,
                Autorizado:   false,
                CStat:        "217",
                NProt:        null,
                XmlProtocolo: null,
                ElapsedMs:    0L)));

        await CreateProcessor().ExecuteAsync(CancellationToken.None);

        doc1.Status.ShouldBe(StatusDocumento.Falhou);
        doc2.Status.ShouldBe(StatusDocumento.Falhou);
        await _sefazClient.Received(2)
            .ConsultarNfeAsync(Arg.Any<string>(), Arg.Any<TenantId>(), Arg.Any<TipoDocumento>(), Arg.Any<CancellationToken>());
        // Cada documento produz um SaveChangesAsync independente.
        await _unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    private void ConfigurarDocumentosTravados(params DocumentoFiscal[] documentos)
        => _documentoRepo
            .GetProcessandoAntigoAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(documentos);

    private void ConfigurarConsultaAutorizado(DocumentoFiscal documento, string protocolo)
        => _sefazClient
            .ConsultarNfeAsync(documento.ChaveAcesso.Valor, documento.TenantId, Arg.Any<TipoDocumento>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new SefazConsultaRetorno(
                Encontrado:   true,
                Autorizado:   true,
                CStat:        "100",
                NProt:        protocolo,
                XmlProtocolo: "<nfeProc/>",
                ElapsedMs:    0L)));

    private void ConfigurarConsultaNaoEncontrado(DocumentoFiscal documento)
        => _sefazClient
            .ConsultarNfeAsync(documento.ChaveAcesso.Valor, documento.TenantId, Arg.Any<TipoDocumento>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new SefazConsultaRetorno(
                Encontrado:   false,
                Autorizado:   false,
                CStat:        "217",
                NProt:        null,
                XmlProtocolo: null,
                ElapsedMs:    0L)));

    // Força o status via reflexão para simular corridas de dados sem APIs públicas de transição.
    private static void ForcarStatus(DocumentoFiscal documento, StatusDocumento status)
    {
        var prop = typeof(DocumentoFiscal).GetProperty(
            nameof(DocumentoFiscal.Status),
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!;
        prop.SetValue(documento, status);
    }
}
