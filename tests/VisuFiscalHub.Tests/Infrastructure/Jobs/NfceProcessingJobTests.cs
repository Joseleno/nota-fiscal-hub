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
using VisuFiscalHub.Infrastructure.Jobs;
using VisuFiscalHub.Infrastructure.Persistence;
using VisuFiscalHub.Tests.Helpers;

namespace VisuFiscalHub.Tests.Infrastructure.Jobs;

/// <summary>
/// Testa o roteamento de ExecuteAsync do FiscalDocumentProcessingJob conforme cStat retornado pela SEFAZ.
/// O DocumentoFiscal real é usado para garantir que as transições de estado são exercitadas
/// com as invariantes do domínio, não apenas com mocks.
/// </summary>
public class FiscalDocumentProcessingJobTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly IDocumentoFiscalRepository _documentoRepo = Substitute.For<IDocumentoFiscalRepository>();
    private readonly ITenantRepository _tenantRepo = Substitute.For<ITenantRepository>();
    private readonly ISefazClient _sefazClient = Substitute.For<ISefazClient>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly TimeProvider _timeProvider = new FixedTimeProvider(FixedNow);

    private const string FakeQrUrl =
        "https://www.sefaz.rs.gov.br/NFCE/NFCE-consulta.aspx?p=43260111222333000181650010000000011000000014|2|1|a3f1c2b4d5e6f7890a1b2c3d4e5f6a7b8c9d0e1f";

    private FiscalDocumentProcessingJob CreateJob()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var dbContext = new ApplicationDbContext(options, NullLoggerFactory.Instance);
        return new(_documentoRepo, _tenantRepo, _sefazClient, _unitOfWork, _timeProvider,
            NullLogger<FiscalDocumentProcessingJob>.Instance, dbContext);
    }

    private (FiscalDocumentProcessingJob job,
             IDocumentoFiscalRepository documentoRepo,
             ITenantRepository tenantRepo,
             ISefazClient sefazClient,
             ApplicationDbContext dbContext,
             IUnitOfWork unitOfWork,
             TimeProvider timeProvider) CriarJobComDbContext()
    {
        var documentoRepo = Substitute.For<IDocumentoFiscalRepository>();
        var tenantRepo    = Substitute.For<ITenantRepository>();
        var sefazClient   = Substitute.For<ISefazClient>();
        var unitOfWork    = Substitute.For<IUnitOfWork>();
        var timeProvider  = new FixedTimeProvider(FixedNow);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var dbContext = new ApplicationDbContext(options, NullLoggerFactory.Instance);

        var job = new FiscalDocumentProcessingJob(
            documentoRepo, tenantRepo, sefazClient, unitOfWork, timeProvider,
            NullLogger<FiscalDocumentProcessingJob>.Instance, dbContext);

        return (job, documentoRepo, tenantRepo, sefazClient, dbContext, unitOfWork, timeProvider);
    }

    // ── documento não encontrado ──────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_DocumentoNaoEncontrado_NaoLancaExcecao()
    {
        var id = DocumentoFiscalId.New();
        _documentoRepo.GetByIdForUpdateAsync(id, Arg.Any<CancellationToken>()).Returns((DocumentoFiscal?)null);

        await Should.NotThrowAsync(() => CreateJob().ExecuteAsync(id, CancellationToken.None));

        await _sefazClient.DidNotReceiveWithAnyArgs().SubmeterAutorizacaoAsync(default!, default, default);
    }

    // ── idempotência: status finais não disparam envio à SEFAZ ───────────────────

    [Theory]
    [InlineData(StatusDocumento.Autorizado)]
    [InlineData(StatusDocumento.Rejeitado)]
    [InlineData(StatusDocumento.Denegado)]
    [InlineData(StatusDocumento.Cancelado)]
    [InlineData(StatusDocumento.Falhou)]
    public async Task ExecuteAsync_StatusFinal_NaoSubmeteParaSefaz(StatusDocumento status)
    {
        var id = DocumentoFiscalId.New();
        var documento = DocumentoFiscalBuilder.EmStatus(status);
        _documentoRepo.GetByIdForUpdateAsync(id, Arg.Any<CancellationToken>()).Returns(documento);

        await CreateJob().ExecuteAsync(id, CancellationToken.None);

        await _sefazClient.DidNotReceiveWithAnyArgs().SubmeterAutorizacaoAsync(default!, default, default);
    }

    // ── Enfileirado: save intermediário ocorre após IniciarProcessamento ─────────
    // O job salva o status Processando antes de chamar SEFAZ para que o ReconciliacaoJobProcessor
    // possa detectar o documento travado em caso de crash após a transição.

    [Fact]
    public async Task ExecuteAsync_Enfileirado_SalvaProcessandoAntesDeSubmeterASefaz()
    {
        var id = DocumentoFiscalId.New();
        var documento = DocumentoFiscalBuilder.Enfileirado(id: id);
        _documentoRepo.GetByIdForUpdateAsync(id, Arg.Any<CancellationToken>()).Returns(documento);

        // SEFAZ falha — queremos confirmar que o save intermediário ocorreu antes da chamada SEFAZ.
        _sefazClient.SubmeterAutorizacaoAsync(id, documento.TenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<SefazRetorno>(new Error("Sefaz.Timeout", "Timeout.")));

        await Should.ThrowAsync<InvalidOperationException>(
            () => CreateJob().ExecuteAsync(id, CancellationToken.None));

        // O save do status Processando ocorre ANTES de chamar SEFAZ — documento ficará rastreável.
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        documento.Status.ShouldBe(StatusDocumento.Processando);
    }

    // ── fluxo normal: Enfileirado → Processando → Autorizado ─────────────────────

    [Fact]
    public async Task ExecuteAsync_Enfileirado_SefazAutorizado_TransicionaParaAutorizado()
    {
        var id = DocumentoFiscalId.New();
        var documento = DocumentoFiscalBuilder.Enfileirado(id: id);
        _documentoRepo.GetByIdForUpdateAsync(id, Arg.Any<CancellationToken>()).Returns(documento);
        ConfigurarSefazAutorizado(id, documento.TenantId, protocolo: "315260000000001");

        await CreateJob().ExecuteAsync(id, CancellationToken.None);

        documento.Status.ShouldBe(StatusDocumento.Autorizado);
        documento.Protocolo.ShouldBe("315260000000001");
        // O job salva duas vezes: (1) após IniciarProcessamento; (2) após Autorizar.
        await _unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
        documento.QrCode.ShouldNotBeNull();
        documento.QrCode!.UrlCompleta.ShouldBe(FakeQrUrl);
    }

    // ── retomada pós-crash: Processando → Autorizado (sem save intermediário) ─────

    [Fact]
    public async Task ExecuteAsync_Processando_SefazAutorizado_TransicionaParaAutorizado()
    {
        var id = DocumentoFiscalId.New();
        var documento = DocumentoFiscalBuilder.Processando(id: id);
        _documentoRepo.GetByIdForUpdateAsync(id, Arg.Any<CancellationToken>()).Returns(documento);
        ConfigurarSefazAutorizado(id, documento.TenantId, protocolo: "315260000000002");

        await CreateJob().ExecuteAsync(id, CancellationToken.None);

        documento.Status.ShouldBe(StatusDocumento.Autorizado);
        // Pós-crash não chama IniciarProcessamento — salva apenas uma vez (após Autorizar).
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        documento.QrCode.ShouldNotBeNull();
        documento.QrCode!.UrlCompleta.ShouldBe(FakeQrUrl);
    }

    // ── autorizado sem NProt → lança para retentar ───────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SefazAutorizadoSemNProt_LancaInvalidOperationException()
    {
        var id = DocumentoFiscalId.New();
        var documento = DocumentoFiscalBuilder.Processando(id: id);
        _documentoRepo.GetByIdForUpdateAsync(id, Arg.Any<CancellationToken>()).Returns(documento);

        _sefazClient.SubmeterAutorizacaoAsync(id, documento.TenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new SefazRetorno(
                Autorizado:    true,
                CStat:         "100",
                XMotivo:       "Autorizado o uso da NF-e",
                NProt:         null,
                XmlAutorizado: "<nfeProc/>",
                QrCodeUrl:     "https://fake-url",
                ElapsedMs:     0L)));

        await Should.ThrowAsync<InvalidOperationException>(
            () => CreateJob().ExecuteAsync(id, CancellationToken.None));
    }

    // ── duplicidade (204 / 572) → consulta SEFAZ → Autorizado ──────────────────

    [Theory]
    [InlineData("204")]
    [InlineData("572")]
    public async Task ExecuteAsync_Duplicidade_ConsultaSefazEAutoriza(string cStat)
    {
        var id = DocumentoFiscalId.New();
        var documento = DocumentoFiscalBuilder.Processando(id: id);
        _documentoRepo.GetByIdForUpdateAsync(id, Arg.Any<CancellationToken>()).Returns(documento);
        ConfigurarSefazRetorno(id, documento.TenantId, autorizado: false, cStat, xMotivo: "Duplicidade de NF-e");

        _sefazClient.ConsultarNfeAsync(documento.ChaveAcesso.Valor, documento.TenantId, Arg.Any<TipoDocumento>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new SefazConsultaRetorno(
                Encontrado:   true,
                Autorizado:   true,
                CStat:        "100",
                NProt:        "135260000099999",
                XmlProtocolo: "<nfeProc/>",
                ElapsedMs:    0L)));

        await CreateJob().ExecuteAsync(id, CancellationToken.None);

        documento.Status.ShouldBe(StatusDocumento.Autorizado);
        documento.Protocolo.ShouldBe("135260000099999");
    }

    [Theory]
    [InlineData("204")]
    [InlineData("572")]
    public async Task ExecuteAsync_Duplicidade_ConsultaSefazFalha_LancaParaRetry(string cStat)
    {
        var id = DocumentoFiscalId.New();
        var documento = DocumentoFiscalBuilder.Processando(id: id);
        _documentoRepo.GetByIdForUpdateAsync(id, Arg.Any<CancellationToken>()).Returns(documento);
        ConfigurarSefazRetorno(id, documento.TenantId, autorizado: false, cStat, xMotivo: "Duplicidade de NF-e");

        _sefazClient.ConsultarNfeAsync(documento.ChaveAcesso.Valor, documento.TenantId, Arg.Any<TipoDocumento>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<SefazConsultaRetorno>(new Error("Sefaz.Timeout", "Timeout na consulta.")));

        await Should.ThrowAsync<InvalidOperationException>(
            () => CreateJob().ExecuteAsync(id, CancellationToken.None));

        documento.Status.ShouldBe(StatusDocumento.Processando);
    }

    // ── denegado (110 / 301 / 302) com tenant null → motivo inclui "CNPJ desconhecido" ──

    [Theory]
    [InlineData("110")]
    [InlineData("301")]
    [InlineData("302")]
    public async Task ExecuteAsync_Denegado_TenantNaoEncontrado_TransicionaParaDenegadoComMotivoCorreto(string cStat)
    {
        var id = DocumentoFiscalId.New();
        var documento = DocumentoFiscalBuilder.Processando(id: id);
        _documentoRepo.GetByIdForUpdateAsync(id, Arg.Any<CancellationToken>()).Returns(documento);
        _tenantRepo.GetByIdAsync(documento.TenantId, Arg.Any<CancellationToken>())
            .Returns((Tenant?)null);
        ConfigurarSefazRetorno(id, documento.TenantId, autorizado: false, cStat, xMotivo: "Uso Denegado");

        await CreateJob().ExecuteAsync(id, CancellationToken.None);

        documento.Status.ShouldBe(StatusDocumento.Denegado);
        documento.MotivoRejeicao.ShouldNotBeNull();
        documento.MotivoRejeicao!.ShouldContain(cStat);
    }

    // ── denegado com tenant real → transiciona para Denegado e salva ────────────
    // Verifica o caminho completo de DenegarAsync quando o CNPJ do emitente está disponível.
    // O CNPJ é passado para documento.Denegar(motivo, cnpjEmitente) e aparece no LogCritical,
    // não no MotivoRejeicao (que contém apenas "[cStat] xMotivo").

    [Fact]
    public async Task ExecuteAsync_Denegado_TenantEncontrado_TransicionaParaDenegadoESalva()
    {
        var id = DocumentoFiscalId.New();
        var documento = DocumentoFiscalBuilder.Processando(id: id);
        _documentoRepo.GetByIdForUpdateAsync(id, Arg.Any<CancellationToken>()).Returns(documento);

        var tenant = CriarTenantFake(documento.ClienteAppId);
        _tenantRepo.GetByIdAsync(documento.TenantId, Arg.Any<CancellationToken>())
            .Returns(tenant);

        ConfigurarSefazRetorno(id, documento.TenantId, autorizado: false, "110", xMotivo: "Uso Denegado");

        await CreateJob().ExecuteAsync(id, CancellationToken.None);

        documento.Status.ShouldBe(StatusDocumento.Denegado);
        documento.MotivoRejeicao.ShouldBe("[110] Uso Denegado");
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── rejeição definitiva (4xx/5xx não mapeados) → Rejeitado ───────────────────

    [Fact]
    public async Task ExecuteAsync_RejeicaoDefinitiva_TransicionaParaRejeitado()
    {
        var id = DocumentoFiscalId.New();
        var documento = DocumentoFiscalBuilder.Processando(id: id);
        _documentoRepo.GetByIdForUpdateAsync(id, Arg.Any<CancellationToken>()).Returns(documento);
        ConfigurarSefazRetorno(id, documento.TenantId, autorizado: false, "400", xMotivo: "Schema Inválido");

        await CreateJob().ExecuteAsync(id, CancellationToken.None);

        documento.Status.ShouldBe(StatusDocumento.Rejeitado);
    }

    // ── rejeição recuperável (1xx exceto 110) → lança para retry Hangfire ────────

    [Theory]
    [InlineData("108")]
    [InlineData("109")]
    public async Task ExecuteAsync_RejeicaoRecuperavel_LancaEMantemProcessando(string cStat)
    {
        var id = DocumentoFiscalId.New();
        var documento = DocumentoFiscalBuilder.Processando(id: id);
        _documentoRepo.GetByIdForUpdateAsync(id, Arg.Any<CancellationToken>()).Returns(documento);
        ConfigurarSefazRetorno(id, documento.TenantId, autorizado: false, cStat, xMotivo: "Serviço em manutenção");

        await Should.ThrowAsync<InvalidOperationException>(
            () => CreateJob().ExecuteAsync(id, CancellationToken.None));

        // Documento permanece em Processando para que o Hangfire possa retentar.
        documento.Status.ShouldBe(StatusDocumento.Processando);
    }

    // ── falha de infraestrutura SEFAZ (HTTP/timeout) → lança, não transiciona ────

    [Fact]
    public async Task ExecuteAsync_FalhaComunicacaoSefaz_LancaEMantemProcessando()
    {
        var id = DocumentoFiscalId.New();
        var documento = DocumentoFiscalBuilder.Processando(id: id);
        _documentoRepo.GetByIdForUpdateAsync(id, Arg.Any<CancellationToken>()).Returns(documento);

        _sefazClient.SubmeterAutorizacaoAsync(id, documento.TenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<SefazRetorno>(new Error("Sefaz.Timeout", "Timeout na comunicação.")));

        await Should.ThrowAsync<InvalidOperationException>(
            () => CreateJob().ExecuteAsync(id, CancellationToken.None));

        // ReconciliacaoJobProcessor detecta o documento travado — não transicionar para Falhou.
        documento.Status.ShouldBe(StatusDocumento.Processando);
    }

    // ── QrCodeUrl propagada ao documento após autorização ────────────────────────

    [Fact]
    public async Task ExecuteAsync_Enfileirado_SefazAutorizado_QrCodeUrlPropagada()
    {
        const string expectedQrUrl = "https://www.sefaz.rs.gov.br/NFCE/NFCE-consulta.aspx?p=43260199988877000195650020000000022000000025|2|1|b4c2d1e0f9a8b7c6d5e4f3a2b1c0d9e8f7a6b5c4d3";
        var id = DocumentoFiscalId.New();
        var documento = DocumentoFiscalBuilder.Enfileirado(id: id);
        _documentoRepo.GetByIdForUpdateAsync(id, Arg.Any<CancellationToken>()).Returns(documento);

        _sefazClient.SubmeterAutorizacaoAsync(id, documento.TenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new SefazRetorno(
                Autorizado:    true,
                CStat:         "100",
                XMotivo:       "Autorizado o uso da NF-e",
                NProt:         "315260000000099",
                XmlAutorizado: "<nfeProc/>",
                QrCodeUrl:     expectedQrUrl,
                ElapsedMs:     0L)));

        await CreateJob().ExecuteAsync(id, CancellationToken.None);

        documento.Status.ShouldBe(StatusDocumento.Autorizado);
        documento.QrCode.ShouldNotBeNull();
        documento.QrCode!.UrlCompleta.ShouldBe(expectedQrUrl);
    }

    // ── duplicidade: QrCode do documento persistido usado; fallback para chave de acesso ──

    [Theory]
    [InlineData("204")]
    [InlineData("572")]
    public async Task ExecuteAsync_Duplicidade_QrCodePersistidoUsadoNaAutorizacao(string cStat)
    {
        const string persistedQrUrl = "https://www.sefaz.rs.gov.br/NFCE/NFCE-consulta.aspx?p=35260112345678000195650010000000011234567810|2|1|abc123";
        var id = DocumentoFiscalId.New();
        var documento = DocumentoFiscalBuilder.Enfileirado(id: id);

        // Simular QrCode já persistido (gerado durante a emissão original)
        var qrCodePersistido = QrCode.FromStorage(persistedQrUrl);
        // Autorizar com QrCode para simular o estado pós-emissão com QrCode salvo
        documento.IniciarProcessamento();
        documento.Autorizar("000000000000001", "<nfeProc/>", qrCodePersistido, FixedNow, _timeProvider);
        // Forçar de volta para Processando para simular re-entrega do job
        var statusProp = typeof(DocumentoFiscal).GetProperty(
            nameof(DocumentoFiscal.Status),
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!;
        statusProp.SetValue(documento, StatusDocumento.Processando);

        _documentoRepo.GetByIdForUpdateAsync(id, Arg.Any<CancellationToken>()).Returns(documento);
        ConfigurarSefazRetorno(id, documento.TenantId, autorizado: false, cStat, xMotivo: "Duplicidade de NF-e");
        _sefazClient.ConsultarNfeAsync(documento.ChaveAcesso.Valor, documento.TenantId, Arg.Any<TipoDocumento>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new SefazConsultaRetorno(
                Encontrado: true, Autorizado: true, CStat: "100",
                NProt: "135260000099999", XmlProtocolo: "<nfeProc/>", ElapsedMs: 0L)));

        await CreateJob().ExecuteAsync(id, CancellationToken.None);

        documento.Status.ShouldBe(StatusDocumento.Autorizado);
        documento.QrCode.ShouldNotBeNull();
        documento.QrCode!.UrlCompleta.ShouldBe(persistedQrUrl);
    }

    [Theory]
    [InlineData("204")]
    [InlineData("572")]
    public async Task ExecuteAsync_Duplicidade_SemQrCodePersistido_UsaChaveAcessoComoFallback(string cStat)
    {
        var id = DocumentoFiscalId.New();
        var documento = DocumentoFiscalBuilder.Processando(id: id);
        _documentoRepo.GetByIdForUpdateAsync(id, Arg.Any<CancellationToken>()).Returns(documento);
        ConfigurarSefazRetorno(id, documento.TenantId, autorizado: false, cStat, xMotivo: "Duplicidade de NF-e");
        _sefazClient.ConsultarNfeAsync(documento.ChaveAcesso.Valor, documento.TenantId, Arg.Any<TipoDocumento>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new SefazConsultaRetorno(
                Encontrado: true, Autorizado: true, CStat: "100",
                NProt: "135260000099998", XmlProtocolo: "<nfeProc/>", ElapsedMs: 0L)));

        await CreateJob().ExecuteAsync(id, CancellationToken.None);

        documento.Status.ShouldBe(StatusDocumento.Autorizado);
        documento.QrCode.ShouldNotBeNull("fallback deve preencher QrCode com chave de acesso");
        documento.QrCode!.UrlCompleta.ShouldBe(documento.ChaveAcesso.Valor);
    }

    // ── guard NFSe: não submete à SEFAZ, falha o documento e retorna ─────────────

    [Fact]
    public async Task ExecuteAsync_NFSe_NaoSubmeteParaSefazEFalhaDocumento()
    {
        var id = DocumentoFiscalId.New();
        var chaveAcesso = ChaveAcesso.Gerar(
            cUF:    35,
            aamm:   "2601",
            cnpj:   "12345678000195",
            mod:    99,
            serie:  "001",
            nNF:    "000000001",
            tpEmis: TipoEmissao.Normal,
            cNF:    "12345678").Value;
        var tributo = Tributo.Criar(
            tipoIcms: TipoIcms.CSOSN, csosnOuCst: 400,
            aliquotaIcms: 0m, baseCalculoIcms: 0m, valorIcms: 0m,
            cstPis: CstPisCofins.Cst07, baseCalculoPis: 0m, aliquotaPis: 0m, valorPis: 0m,
            cstCofins: CstPisCofins.Cst07, baseCalculoCofins: 0m, aliquotaCofins: 0m, valorCofins: 0m).Value;
        var produto = Produto.Criar(
            codigoProduto: "PROD001", descricao: "Produto Teste", ncm: "12345678", cest: null,
            cfopSaida: "5102", unidadeComercial: "UN", quantidade: 1m, valorUnitario: 10m,
            valorDesconto: 0m, origemMercadoria: OrigemMercadoria.Nacional).Value;
        var item = new ItemDocumento(1, produto, tributo);
        var pagamento = Pagamento.Criar(TipoPagamento.Dinheiro, 10m).Value;

        var documento = DocumentoFiscal.Criar(
            id:             id,
            tenantId:       new TenantId(Guid.NewGuid()),
            clienteAppId:   ClienteAppId.New(),
            idempotencyKey: $"idem-nfse-{id.Value}",
            tipo:           TipoDocumento.NFSe,
            chaveAcesso:    chaveAcesso,
            numero:         1,
            serie:          "001",
            indPresenca:    1,
            items:          [item],
            pagamentos:     [pagamento],
            timeProvider:   TimeProvider.System).Value;

        documento.Enfileirar().IsSuccess.ShouldBeTrue();

        _documentoRepo.GetByIdForUpdateAsync(id, Arg.Any<CancellationToken>()).Returns(documento);

        await CreateJob().ExecuteAsync(id, CancellationToken.None);

        // Guard: não deve chamar SEFAZ
        await _sefazClient.DidNotReceiveWithAnyArgs().SubmeterAutorizacaoAsync(default!, default, default);

        // Guard: documento deve estar em Falhou
        documento.Status.ShouldBe(StatusDocumento.Falhou);
    }

    // ── DeliveryAttempt registrado em cada resultado SEFAZ ───────────────────────

    [Fact]
    public async Task ExecuteAsync_SefazAutorizado_CriaDeliveryAttemptEnvio()
    {
        var (job, documentoRepo, _, sefazClient, dbContext, unitOfWork, _) = CriarJobComDbContext();
        var doc = DocumentoFiscalBuilder.Enfileirado();
        documentoRepo.GetByIdForUpdateAsync(doc.Id, Arg.Any<CancellationToken>()).Returns(doc);
        sefazClient.SubmeterAutorizacaoAsync(doc.Id, doc.TenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new SefazRetorno(
                Autorizado: true, CStat: "100", XMotivo: "Autorizado",
                NProt: "135260000000001", XmlAutorizado: "<protNFe/>",
                QrCodeUrl: FakeQrUrl, ElapsedMs: 150L)));

        await job.ExecuteAsync(doc.Id, CancellationToken.None);

        dbContext.DeliveryAttempts.Local.ShouldContain(a =>
            a.DocumentoFiscalId == doc.Id &&
            a.TipoTentativa == TipoTentativa.Envio &&
            a.Success &&
            a.ResponseCode == "100" &&
            a.ElapsedMs == 150L);
    }

    [Fact]
    public async Task ExecuteAsync_SefazRejeitado_CriaDeliveryAttemptEnvioFalso()
    {
        var (job, documentoRepo, _, sefazClient, dbContext, unitOfWork, _) = CriarJobComDbContext();
        var doc = DocumentoFiscalBuilder.Enfileirado();
        documentoRepo.GetByIdForUpdateAsync(doc.Id, Arg.Any<CancellationToken>()).Returns(doc);
        sefazClient.SubmeterAutorizacaoAsync(doc.Id, doc.TenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new SefazRetorno(
                Autorizado: false, CStat: "999", XMotivo: "Rejeição",
                NProt: null, XmlAutorizado: null,
                QrCodeUrl: null, ElapsedMs: 80L)));

        await job.ExecuteAsync(doc.Id, CancellationToken.None);

        dbContext.DeliveryAttempts.Local.ShouldContain(a =>
            a.DocumentoFiscalId == doc.Id &&
            a.TipoTentativa == TipoTentativa.Envio &&
            !a.Success &&
            a.ElapsedMs == 80L);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    private void ConfigurarSefazAutorizado(DocumentoFiscalId id, TenantId tenantId, string protocolo)
        => _sefazClient.SubmeterAutorizacaoAsync(id, tenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new SefazRetorno(
                Autorizado:    true,
                CStat:         "100",
                XMotivo:       "Autorizado o uso da NF-e",
                NProt:         protocolo,
                XmlAutorizado: "<nfeProc/>",
                QrCodeUrl:     FakeQrUrl,
                ElapsedMs:     0L)));

    private void ConfigurarSefazRetorno(
        DocumentoFiscalId id,
        TenantId tenantId,
        bool autorizado,
        string cStat,
        string xMotivo)
        => _sefazClient.SubmeterAutorizacaoAsync(id, tenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new SefazRetorno(
                Autorizado:    autorizado,
                CStat:         cStat,
                XMotivo:       xMotivo,
                NProt:         null,
                XmlAutorizado: null,
                QrCodeUrl:     null,
                ElapsedMs:     0L)));

    private static Tenant CriarTenantFake(ClienteAppId clienteAppId)
    {
        var cnpj = Cnpj.Criar("11.222.333/0001-81").Value;
        var config = ConfiguracaoFiscal.Criar(
            RegimeTributario.SimplesNacional, "001", AmbienteSefaz.Homologacao, 35).Value;
        var endereco = Endereco.Criar(
            "Rua Teste", "100", null, "Centro", "São Paulo", 3550308, "SP", "01310100").Value;

        return Tenant.Criar(clienteAppId, cnpj, "Empresa Teste", null, config, endereco,
            TimeProvider.System).Value;
    }
}
