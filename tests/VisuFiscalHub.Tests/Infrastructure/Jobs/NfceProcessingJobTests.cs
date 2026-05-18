using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
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
/// Testa o roteamento de ExecuteAsync do NfceProcessingJob conforme cStat retornado pela SEFAZ.
/// O DocumentoFiscal real é usado para garantir que as transições de estado são exercitadas
/// com as invariantes do domínio, não apenas com mocks.
/// </summary>
public class NfceProcessingJobTests
{
    private readonly IDocumentoFiscalRepository _documentoRepo = Substitute.For<IDocumentoFiscalRepository>();
    private readonly ITenantRepository _tenantRepo = Substitute.For<ITenantRepository>();
    private readonly ISefazClient _sefazClient = Substitute.For<ISefazClient>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly TimeProvider _timeProvider = new FixedTimeProvider(
        new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero));

    private NfceProcessingJob CreateJob() =>
        new(_documentoRepo, _tenantRepo, _sefazClient, _unitOfWork, _timeProvider,
            NullLogger<NfceProcessingJob>.Instance);

    // ── documento não encontrado ──────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_DocumentoNaoEncontrado_NaoLancaExcecao()
    {
        var id = DocumentoFiscalId.New();
        _documentoRepo.GetByIdForUpdateAsync(id, Arg.Any<CancellationToken>()).Returns((DocumentoFiscal?)null);

        var job = CreateJob();

        await Should.NotThrowAsync(() => job.ExecuteAsync(id, CancellationToken.None));
        await _sefazClient.DidNotReceiveWithAnyArgs().SubmeterAutorizacaoAsync(default!, default, default);
    }

    // ── idempotência: status finais ───────────────────────────────────────────────

    [Theory]
    [InlineData(StatusDocumento.Autorizado)]
    [InlineData(StatusDocumento.Rejeitado)]
    [InlineData(StatusDocumento.Denegado)]
    [InlineData(StatusDocumento.Cancelado)]
    [InlineData(StatusDocumento.Falhou)]
    public async Task ExecuteAsync_StatusFinal_NaoSubmeteParaSefaz(StatusDocumento status)
    {
        var id = DocumentoFiscalId.New();
        var documento = BuildDocumentoEmStatus(status);
        _documentoRepo.GetByIdForUpdateAsync(id, Arg.Any<CancellationToken>()).Returns(documento);

        var job = CreateJob();

        await job.ExecuteAsync(id, CancellationToken.None);

        await _sefazClient.DidNotReceiveWithAnyArgs().SubmeterAutorizacaoAsync(default!, default, default);
    }

    // ── fluxo normal: Enfileirado → Processando → Autorizado ─────────────────────

    [Fact]
    public async Task ExecuteAsync_Enfileirado_SefazAutorizado_TransicionaParaAutorizado()
    {
        var id = DocumentoFiscalId.New();
        var documento = BuildDocumentoEnfileirado(id);
        _documentoRepo.GetByIdForUpdateAsync(id, Arg.Any<CancellationToken>()).Returns(documento);

        _sefazClient.SubmeterAutorizacaoAsync(id, documento.TenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new SefazRetorno(
                Autorizado:   true,
                CStat:        "100",
                XMotivo:      "Autorizado o uso da NF-e",
                NProt:        "315260000000001",
                XmlAutorizado: "<nfeProc/>")));

        var job = CreateJob();

        await job.ExecuteAsync(id, CancellationToken.None);

        documento.Status.ShouldBe(StatusDocumento.Autorizado);
        documento.Protocolo.ShouldBe("315260000000001");
        await _unitOfWork.Received().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── Processando (retomada pós-crash) → Autorizado ────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Processando_SefazAutorizado_TransicionaParaAutorizado()
    {
        var id = DocumentoFiscalId.New();
        var documento = BuildDocumentoProcessando(id);
        _documentoRepo.GetByIdForUpdateAsync(id, Arg.Any<CancellationToken>()).Returns(documento);

        _sefazClient.SubmeterAutorizacaoAsync(id, documento.TenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new SefazRetorno(
                Autorizado:   true,
                CStat:        "100",
                XMotivo:      "Autorizado o uso da NF-e",
                NProt:        "315260000000002",
                XmlAutorizado: "<nfeProc/>")));

        var job = CreateJob();

        await job.ExecuteAsync(id, CancellationToken.None);

        documento.Status.ShouldBe(StatusDocumento.Autorizado);
    }

    // ── Autorizado sem NProt ──────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SefazAutorizadoSemNProt_LancaInvalidOperationException()
    {
        var id = DocumentoFiscalId.New();
        var documento = BuildDocumentoProcessando(id);
        _documentoRepo.GetByIdForUpdateAsync(id, Arg.Any<CancellationToken>()).Returns(documento);

        _sefazClient.SubmeterAutorizacaoAsync(id, documento.TenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new SefazRetorno(
                Autorizado:   true,
                CStat:        "100",
                XMotivo:      "Autorizado o uso da NF-e",
                NProt:        null,
                XmlAutorizado: "<nfeProc/>")));

        var job = CreateJob();

        await Should.ThrowAsync<InvalidOperationException>(() => job.ExecuteAsync(id, CancellationToken.None));
    }

    // ── duplicidade (204 / 572) → Rejeitado ──────────────────────────────────────

    [Theory]
    [InlineData("204")]
    [InlineData("572")]
    public async Task ExecuteAsync_Duplicidade_TransicionaParaRejeitado(string cStat)
    {
        var id = DocumentoFiscalId.New();
        var documento = BuildDocumentoProcessando(id);
        _documentoRepo.GetByIdForUpdateAsync(id, Arg.Any<CancellationToken>()).Returns(documento);

        _sefazClient.SubmeterAutorizacaoAsync(id, documento.TenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new SefazRetorno(
                Autorizado:   false,
                CStat:        cStat,
                XMotivo:      "Duplicidade de NF-e",
                NProt:        null,
                XmlAutorizado: null)));

        var job = CreateJob();

        await job.ExecuteAsync(id, CancellationToken.None);

        documento.Status.ShouldBe(StatusDocumento.Rejeitado);
        documento.MotivoRejeicao.ShouldNotBeNull();
        documento.MotivoRejeicao!.ShouldContain("Duplicidade");
    }

    // ── denegado (110 / 301 / 302) → Denegado ────────────────────────────────────

    [Theory]
    [InlineData("110")]
    [InlineData("301")]
    [InlineData("302")]
    public async Task ExecuteAsync_Denegado_TransicionaParaDenegado(string cStat)
    {
        var id = DocumentoFiscalId.New();
        var documento = BuildDocumentoProcessando(id);
        _documentoRepo.GetByIdForUpdateAsync(id, Arg.Any<CancellationToken>()).Returns(documento);

        _tenantRepo.GetByIdAsync(documento.TenantId, Arg.Any<CancellationToken>())
            .Returns((Domain.Entities.Tenant?)null);

        _sefazClient.SubmeterAutorizacaoAsync(id, documento.TenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new SefazRetorno(
                Autorizado:   false,
                CStat:        cStat,
                XMotivo:      "Uso Denegado",
                NProt:        null,
                XmlAutorizado: null)));

        var job = CreateJob();

        await job.ExecuteAsync(id, CancellationToken.None);

        documento.Status.ShouldBe(StatusDocumento.Denegado);
        documento.MotivoRejeicao.ShouldNotBeNull();
        documento.MotivoRejeicao!.ShouldContain(cStat);
    }

    // ── rejeição definitiva não-denegada (ex: 4xx) → Rejeitado ───────────────────

    [Fact]
    public async Task ExecuteAsync_RejeicaoDefinitiva_TransicionaParaRejeitado()
    {
        var id = DocumentoFiscalId.New();
        var documento = BuildDocumentoProcessando(id);
        _documentoRepo.GetByIdForUpdateAsync(id, Arg.Any<CancellationToken>()).Returns(documento);

        _sefazClient.SubmeterAutorizacaoAsync(id, documento.TenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new SefazRetorno(
                Autorizado:   false,
                CStat:        "400",
                XMotivo:      "Schema Inválido",
                NProt:        null,
                XmlAutorizado: null)));

        var job = CreateJob();

        await job.ExecuteAsync(id, CancellationToken.None);

        documento.Status.ShouldBe(StatusDocumento.Rejeitado);
    }

    // ── rejeição recuperável (1xx, exceto 110) → lança para retry Hangfire ────────

    [Theory]
    [InlineData("109")]
    [InlineData("108")]
    public async Task ExecuteAsync_RejeicaoRecuperavel_LancaInvalidOperationException(string cStat)
    {
        var id = DocumentoFiscalId.New();
        var documento = BuildDocumentoProcessando(id);
        _documentoRepo.GetByIdForUpdateAsync(id, Arg.Any<CancellationToken>()).Returns(documento);

        _sefazClient.SubmeterAutorizacaoAsync(id, documento.TenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new SefazRetorno(
                Autorizado:   false,
                CStat:        cStat,
                XMotivo:      "Serviço em manutenção",
                NProt:        null,
                XmlAutorizado: null)));

        var job = CreateJob();

        await Should.ThrowAsync<InvalidOperationException>(() => job.ExecuteAsync(id, CancellationToken.None));

        // Documento permanece em Processando para que o Hangfire possa retentar.
        documento.Status.ShouldBe(StatusDocumento.Processando);
    }

    // ── falha de infraestrutura SEFAZ (HTTP/timeout) → lança para retry ──────────

    [Fact]
    public async Task ExecuteAsync_FalhaInfraestruturasSefaz_LancaInvalidOperationException()
    {
        var id = DocumentoFiscalId.New();
        var documento = BuildDocumentoProcessando(id);
        _documentoRepo.GetByIdForUpdateAsync(id, Arg.Any<CancellationToken>()).Returns(documento);

        _sefazClient.SubmeterAutorizacaoAsync(id, documento.TenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<SefazRetorno>(new Error("Sefaz.Timeout", "Timeout na comunicação.")));

        var job = CreateJob();

        await Should.ThrowAsync<InvalidOperationException>(() => job.ExecuteAsync(id, CancellationToken.None));

        // Documento permanece em Processando — não transitado para Falhou.
        documento.Status.ShouldBe(StatusDocumento.Processando);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    // Constrói DocumentoFiscal em estado Enfileirado (pronto para IniciarProcessamento).
    private DocumentoFiscal BuildDocumentoEnfileirado(DocumentoFiscalId? id = null)
    {
        var doc = BuildDocumentoBase(id);
        doc.Enfileirar().IsSuccess.ShouldBeTrue();
        return doc;
    }

    // Constrói DocumentoFiscal em estado Processando (pós-crash ou recomeço).
    private DocumentoFiscal BuildDocumentoProcessando(DocumentoFiscalId? id = null)
    {
        var doc = BuildDocumentoEnfileirado(id);
        doc.IniciarProcessamento().IsSuccess.ShouldBeTrue();
        return doc;
    }

    // Constrói DocumentoFiscal em status arbitrário por reflexão para testar idempotência.
    // Necessário porque não há transição direta para Autorizado/Rejeitado/Falhou sem passar
    // pelo fluxo normal — mas o teste de idempotência não deve depender do fluxo SEFAZ.
    private static DocumentoFiscal BuildDocumentoEmStatus(StatusDocumento status)
    {
        var doc = BuildDocumentoBaseStatic();

        if (status == StatusDocumento.Enfileirado || status == StatusDocumento.Criado)
        {
            if (status == StatusDocumento.Enfileirado)
                doc.Enfileirar();
            return doc;
        }

        // Leva o documento a Processando (pré-requisito para as transições finais).
        doc.Enfileirar();
        doc.IniciarProcessamento();

        var statusProp = typeof(DocumentoFiscal).GetProperty(
            nameof(DocumentoFiscal.Status),
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!;

        statusProp.SetValue(doc, status);

        return doc;
    }

    private DocumentoFiscal BuildDocumentoBase(DocumentoFiscalId? id = null)
    {
        var docId = id ?? DocumentoFiscalId.New();
        return BuildDocumentoBaseStatic(docId);
    }

    private static DocumentoFiscal BuildDocumentoBaseStatic(DocumentoFiscalId? id = null)
    {
        var docId = id ?? DocumentoFiscalId.New();
        var tenantId = new TenantId(Guid.NewGuid());
        var clienteAppId = ClienteAppId.New();

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

        return DocumentoFiscal.Criar(
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
    }

    private sealed class FixedTimeProvider(DateTimeOffset fixedNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => fixedNow;
    }
}
