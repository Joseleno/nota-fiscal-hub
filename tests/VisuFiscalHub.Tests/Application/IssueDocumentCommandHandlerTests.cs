using NSubstitute;
using Shouldly;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Application.Documents.Commands.IssueDocument;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.Interfaces;
using VisuFiscalHub.Domain.ValueObjects;

namespace VisuFiscalHub.Tests.Application;

public class IssueDocumentCommandHandlerTests
{
    // ──────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────

    private static Tenant CriarTenantAtivo()
    {
        var cnpj = Cnpj.Criar("11.222.333/0001-81").Value;
        var config = ConfiguracaoFiscal.Criar(
            RegimeTributario.SimplesNacional, "001", AmbienteSefaz.Homologacao, 35).Value;
        var endereco = Endereco.Criar(
            "Rua Teste", "100", null, "Centro", "São Paulo", 3550308, "SP", "01310100").Value;
        return Tenant.Criar(
            ClienteAppId.New(), cnpj, "Empresa Teste", null, config, endereco, TimeProvider.System).Value;
    }

    private static IssueDocumentCommand CmdBase(
        TenantId tenantId,
        ClienteAppId clienteAppId,
        string? cpf = null,
        string? nomeConsumidor = null) => new()
    {
        TenantId = tenantId,
        ClienteAppId = clienteAppId,
        IdempotencyKey = $"idem-{Guid.NewGuid()}",
        Tipo = TipoDocumento.NfCe,
        IndPresenca = 1,
        Itens =
        [
            new ItemDocumentoDto(
                CodigoProduto: "P001",
                Descricao: "Produto Teste",
                Ncm: "12345678",
                Cest: null,
                CfopSaida: "5102",
                UnidadeComercial: "UN",
                Quantidade: 1m,
                ValorUnitario: 10m,
                ValorDesconto: 0m,
                OrigemMercadoria: OrigemMercadoria.Nacional,
                Tributo: new TributoDto(
                    TipoIcms.CSOSN, 400,
                    0m, 0m, 0m,
                    CstPisCofins.Cst07, 0m, 0m, 0m,
                    CstPisCofins.Cst07, 0m, 0m, 0m))
        ],
        Pagamentos = [new PagamentoDto(TipoPagamento.Dinheiro, 10m)],
        Consumidor = cpf is not null || nomeConsumidor is not null
            ? new ConsumidorDto(cpf, nomeConsumidor)
            : null
    };

    private static (IssueDocumentCommandHandler handler,
                    IDocumentoFiscalRepository docRepo,
                    ITenantRepository tenantRepo,
                    ISequenceManager seqManager,
                    IUnitOfWork unitOfWork,
                    IDocumentJobQueue jobQueue)
        CriarHandler(Tenant tenant)
    {
        var docRepo = Substitute.For<IDocumentoFiscalRepository>();
        var tenantRepo = Substitute.For<ITenantRepository>();
        var seqManager = Substitute.For<ISequenceManager>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var jobQueue = Substitute.For<IDocumentJobQueue>();

        docRepo.GetByIdempotencyKeyAsync(Arg.Any<string>(), Arg.Any<TenantId>(), Arg.Any<CancellationToken>())
               .Returns((DocumentoFiscal?)null);

        tenantRepo.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>())
                  .Returns(tenant);

        seqManager.GetNextNumeroAsync(tenant.Id, tenant.ConfiguracaoFiscal.Serie, Arg.Any<CancellationToken>())
                  .Returns(Result.Success(1L));

        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
                  .Returns(Task.FromResult(1));

        jobQueue.EnqueueProcessingAsync(Arg.Any<DocumentoFiscalId>(), Arg.Any<TipoDocumento>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

        var handler = new IssueDocumentCommandHandler(
            docRepo, tenantRepo, seqManager, unitOfWork, jobQueue, TimeProvider.System);

        return (handler, docRepo, tenantRepo, seqManager, unitOfWork, jobQueue);
    }

    // ──────────────────────────────────────────────────────────────
    // Testes — Consumidor
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ComCpfConsumidor_DevePersistirCpfNoDocumento()
    {
        var tenant = CriarTenantAtivo();
        var (handler, docRepo, _, _, _, _) = CriarHandler(tenant);
        // Nome obrigatório quando CPF é informado (invariante domain: schema NF-e exige xNome em <dest>)
        var command = CmdBase(tenant.Id, tenant.ClienteAppId, cpf: "52998224725", nomeConsumidor: "Consumidor Teste");

        await handler.Handle(command, CancellationToken.None);

        await docRepo.Received(1).AddAsync(
            Arg.Is<DocumentoFiscal>(d => d.CpfConsumidor == "52998224725"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ComNomeConsumidor_DevePersistirNomeNoDocumento()
    {
        var tenant = CriarTenantAtivo();
        var (handler, docRepo, _, _, _, _) = CriarHandler(tenant);
        var command = CmdBase(tenant.Id, tenant.ClienteAppId, cpf: "52998224725", nomeConsumidor: "João Silva");

        await handler.Handle(command, CancellationToken.None);

        await docRepo.Received(1).AddAsync(
            Arg.Is<DocumentoFiscal>(d => d.NomeConsumidor == "João Silva"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SemConsumidor_DevePersistirDocumentoComCpfNulo()
    {
        var tenant = CriarTenantAtivo();
        var (handler, docRepo, _, _, _, _) = CriarHandler(tenant);
        var command = CmdBase(tenant.Id, tenant.ClienteAppId);

        await handler.Handle(command, CancellationToken.None);

        await docRepo.Received(1).AddAsync(
            Arg.Is<DocumentoFiscal>(d => d.CpfConsumidor == null && d.NomeConsumidor == null),
            Arg.Any<CancellationToken>());
    }
}
