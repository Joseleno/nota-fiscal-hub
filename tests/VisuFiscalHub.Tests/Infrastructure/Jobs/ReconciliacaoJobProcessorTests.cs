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

        var processor = CreateProcessor();

        await processor.ExecuteAsync(CancellationToken.None);

        await _sefazClient.DidNotReceiveWithAnyArgs()
            .ConsultarNfeAsync(default!, default, default);
    }

    // ── threshold passado ao repositório ─────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_PassaThresholdCorreto_10MinutosAtras()
    {
        // Threshold = FixedNow − 10 min.
        var expectedThreshold = FixedNow.AddMinutes(-10);

        _documentoRepo
            .GetProcessandoAntigoAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<DocumentoFiscal>());

        var processor = CreateProcessor();

        await processor.ExecuteAsync(CancellationToken.None);

        await _documentoRepo.Received(1)
            .GetProcessandoAntigoAsync(
                Arg.Is<DateTimeOffset>(t => t == expectedThreshold),
                Arg.Any<CancellationToken>());
    }

    // ── consulta SEFAZ falhou → reenfileira ──────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ConsultaSefazFalhou_ReenfileiraProcessingJob()
    {
        var documento = BuildDocumentoProcessando();
        _documentoRepo
            .GetProcessandoAntigoAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new[] { documento });

        _sefazClient.ConsultarNfeAsync(
                documento.ChaveAcesso.Valor, documento.TenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<SefazConsultaRetorno>(
                new Error("Sefaz.Timeout", "Timeout na consulta.")));

        var processor = CreateProcessor();

        await processor.ExecuteAsync(CancellationToken.None);

        await _documentJobQueue.Received(1)
            .EnqueueProcessingAsync(documento.Id, Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    // ── SEFAZ autorizado → Autorizado ────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SefazAutorizado_TransicionaParaAutorizado()
    {
        var documento = BuildDocumentoProcessando();
        _documentoRepo
            .GetProcessandoAntigoAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new[] { documento });

        _sefazClient.ConsultarNfeAsync(
                documento.ChaveAcesso.Valor, documento.TenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new SefazConsultaRetorno(
                Encontrado:  true,
                Autorizado:  true,
                CStat:       "100",
                NProt:       "315260000000001",
                XmlProtocolo: "<nfeProc/>")));

        var processor = CreateProcessor();

        await processor.ExecuteAsync(CancellationToken.None);

        documento.Status.ShouldBe(StatusDocumento.Autorizado);
        documento.Protocolo.ShouldBe("315260000000001");
        await _unitOfWork.Received().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── SEFAZ autorizado mas NProt ausente → Falhou ───────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SefazAutorizadoSemNProt_TransicionaParaFalhou()
    {
        var documento = BuildDocumentoProcessando();
        _documentoRepo
            .GetProcessandoAntigoAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new[] { documento });

        _sefazClient.ConsultarNfeAsync(
                documento.ChaveAcesso.Valor, documento.TenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new SefazConsultaRetorno(
                Encontrado:  true,
                Autorizado:  true,
                CStat:       "100",
                NProt:       null,
                XmlProtocolo: null)));

        var processor = CreateProcessor();

        await processor.ExecuteAsync(CancellationToken.None);

        documento.Status.ShouldBe(StatusDocumento.Falhou);
    }

    // ── documento não encontrado na SEFAZ → Falhou ────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_NaoEncontradoNaSefaz_TransicionaParaFalhou()
    {
        var documento = BuildDocumentoProcessando();
        _documentoRepo
            .GetProcessandoAntigoAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new[] { documento });

        _sefazClient.ConsultarNfeAsync(
                documento.ChaveAcesso.Valor, documento.TenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new SefazConsultaRetorno(
                Encontrado:  false,
                Autorizado:  false,
                CStat:       "217",
                NProt:       null,
                XmlProtocolo: null)));

        var processor = CreateProcessor();

        await processor.ExecuteAsync(CancellationToken.None);

        documento.Status.ShouldBe(StatusDocumento.Falhou);
    }

    // ── encontrado mas não autorizado (rejeição definitiva) → Falhou ─────────────

    [Fact]
    public async Task ExecuteAsync_EncontradoNaoAutorizado_TransicionaParaFalhou()
    {
        var documento = BuildDocumentoProcessando();
        _documentoRepo
            .GetProcessandoAntigoAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new[] { documento });

        _sefazClient.ConsultarNfeAsync(
                documento.ChaveAcesso.Valor, documento.TenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new SefazConsultaRetorno(
                Encontrado:  true,
                Autorizado:  false,
                CStat:       "110",
                NProt:       null,
                XmlProtocolo: null)));

        var processor = CreateProcessor();

        await processor.ExecuteAsync(CancellationToken.None);

        documento.Status.ShouldBe(StatusDocumento.Falhou);
    }

    // ── transição concorrente: Falhar() retorna falha → não propaga exceção ───────
    // Representa o cenário onde NfceProcessingJob já transitou o documento entre
    // GetProcessandoAntigoAsync e a chamada de Falhar() na reconciliação.

    [Fact]
    public async Task ExecuteAsync_FalharRetornaFailure_NaoPropagaExcecao()
    {
        var documento = BuildDocumentoProcessando();

        // Simular transição concorrente: levar o documento para Autorizado antes da reconciliação
        // de modo que Falhar() retorne failure (status não é Processando).
        var forcaTransicao = typeof(DocumentoFiscal).GetProperty(
            nameof(DocumentoFiscal.Status),
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!;
        forcaTransicao.SetValue(documento, StatusDocumento.Autorizado);

        _documentoRepo
            .GetProcessandoAntigoAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new[] { documento });

        _sefazClient.ConsultarNfeAsync(
                documento.ChaveAcesso.Valor, documento.TenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new SefazConsultaRetorno(
                Encontrado:  false,
                Autorizado:  false,
                CStat:       "217",
                NProt:       null,
                XmlProtocolo: null)));

        var processor = CreateProcessor();

        // Não deve lançar — transição concorrente é esperada (LogInformation, não LogError).
        await Should.NotThrowAsync(() => processor.ExecuteAsync(CancellationToken.None));

        // Não salva: não houve transição bem-sucedida.
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    // ── múltiplos documentos: todos reconciliados ─────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_DoisDocumentosTravados_AmbosReconciliados()
    {
        var doc1 = BuildDocumentoProcessando();
        var doc2 = BuildDocumentoProcessando();

        _documentoRepo
            .GetProcessandoAntigoAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new[] { doc1, doc2 });

        _sefazClient.ConsultarNfeAsync(
                Arg.Any<string>(), Arg.Any<TenantId>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new SefazConsultaRetorno(
                Encontrado:  false,
                Autorizado:  false,
                CStat:       "217",
                NProt:       null,
                XmlProtocolo: null)));

        var processor = CreateProcessor();

        await processor.ExecuteAsync(CancellationToken.None);

        doc1.Status.ShouldBe(StatusDocumento.Falhou);
        doc2.Status.ShouldBe(StatusDocumento.Falhou);
        await _sefazClient.Received(2)
            .ConsultarNfeAsync(Arg.Any<string>(), Arg.Any<TenantId>(), Arg.Any<CancellationToken>());
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    private static DocumentoFiscal BuildDocumentoProcessando()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        var clienteAppId = ClienteAppId.New();
        var docId = DocumentoFiscalId.New();

        var chaveAcesso = ChaveAcesso.Gerar(
            cUF: 35,
            aamm: "2601",
            cnpj: "12345678000195",
            mod: 65,
            serie: "001",
            nNF: "000000001",
            tpEmis: TipoEmissao.Normal,
            cNF: "12345678").Value;

        var tributo = Tributo.Criar(
            tipoIcms: TipoIcms.CSOSN,
            csosnOuCst: 400,
            aliquotaIcms: 0m,
            baseCalculoIcms: 0m,
            valorIcms: 0m,
            cstPis: CstPisCofins.Cst07,
            baseCalculoPis: 0m,
            aliquotaPis: 0m,
            valorPis: 0m,
            cstCofins: CstPisCofins.Cst07,
            baseCalculoCofins: 0m,
            aliquotaCofins: 0m,
            valorCofins: 0m).Value;

        var produto = Produto.Criar(
            codigoProduto: "PROD001",
            descricao: "Produto Teste",
            ncm: "12345678",
            cest: null,
            cfopSaida: "5102",
            unidadeComercial: "UN",
            quantidade: 1m,
            valorUnitario: 10m,
            valorDesconto: 0m,
            origemMercadoria: OrigemMercadoria.Nacional).Value;

        var item = new ItemDocumento(1, produto, tributo);
        var pagamento = Pagamento.Criar(TipoPagamento.Dinheiro, 10m).Value;

        var doc = DocumentoFiscal.Criar(
            id: docId,
            tenantId: tenantId,
            clienteAppId: clienteAppId,
            idempotencyKey: $"idem-{docId.Value}",
            tipo: TipoDocumento.NfCe,
            chaveAcesso: chaveAcesso,
            numero: 1,
            serie: "001",
            indPresenca: 1,
            items: [item],
            pagamentos: [pagamento],
            timeProvider: TimeProvider.System).Value;

        doc.Enfileirar();
        doc.IniciarProcessamento();

        return doc;
    }

    private sealed class FixedTimeProvider(DateTimeOffset fixedNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => fixedNow;
    }
}
