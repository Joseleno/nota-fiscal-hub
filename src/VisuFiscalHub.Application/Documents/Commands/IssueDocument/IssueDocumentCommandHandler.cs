using System.Security.Cryptography;
using Mediator;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Errors;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.Interfaces;
using VisuFiscalHub.Domain.ValueObjects;

namespace VisuFiscalHub.Application.Documents.Commands.IssueDocument;

public sealed class IssueDocumentCommandHandler
    : ICommandHandler<IssueDocumentCommand, Result<IssueDocumentResponse>>
{
    private readonly IDocumentoFiscalRepository _documentoRepo;
    private readonly ITenantRepository _tenantRepo;
    private readonly ISequenceManager _sequenceManager;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDocumentJobQueue _jobQueue;
    private readonly TimeProvider _timeProvider;

    public IssueDocumentCommandHandler(
        IDocumentoFiscalRepository documentoRepo,
        ITenantRepository tenantRepo,
        ISequenceManager sequenceManager,
        IUnitOfWork unitOfWork,
        IDocumentJobQueue jobQueue,
        TimeProvider timeProvider)
    {
        _documentoRepo = documentoRepo;
        _tenantRepo = tenantRepo;
        _sequenceManager = sequenceManager;
        _unitOfWork = unitOfWork;
        _jobQueue = jobQueue;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<IssueDocumentResponse>> Handle(
        IssueDocumentCommand command,
        CancellationToken cancellationToken)
    {
        // 1. Idempotency check
        var existente = await _documentoRepo.GetByIdempotencyKeyAsync(
            command.IdempotencyKey, command.TenantId, cancellationToken);

        if (existente is not null)
        {
            // Status final — 409
            if (existente.Status is StatusDocumento.Autorizado
                or StatusDocumento.Rejeitado
                or StatusDocumento.Cancelado
                or StatusDocumento.Falhou
                or StatusDocumento.Denegado)
                return Result.Failure<IssueDocumentResponse>(DocumentoFiscalErrors.IdempotencyKeyJaUsada);

            // Em processamento — retorna 200 com documento existente
            return Result.Success(MapToResponse(existente));
        }

        // 2. Carregar Tenant e validar pertencimento
        var tenant = await _tenantRepo.GetByIdAsync(command.TenantId, cancellationToken);
        if (tenant is null || !tenant.IsActive)
            return Result.Failure<IssueDocumentResponse>(TenantErrors.NaoEncontrado);

        if (tenant.ClienteAppId != command.ClienteAppId)
            return Result.Failure<IssueDocumentResponse>(TenantErrors.NaoPertenceAoClienteApp);

        // 3. Construir itens e pagamentos antes de consumir a sequence
        var itemsResult = BuildItems(command.Itens);
        if (itemsResult.IsFailure)
            return Result.Failure<IssueDocumentResponse>(itemsResult.Error);

        var pagamentosResult = BuildPagamentos(command.Pagamentos);
        if (pagamentosResult.IsFailure)
            return Result.Failure<IssueDocumentResponse>(pagamentosResult.Error);

        // 4. Obter próximo número da sequence (após validações — evita consumir número em caso de falha).
        // ATENÇÃO: sequences PostgreSQL são não-transacionais. A partir deste ponto, qualquer falha
        // (ChaveAcesso.Gerar, DocumentoFiscal.Criar) produz uma lacuna permanente na numeração.
        // Manter ChaveAcesso.Gerar e DocumentoFiscal.Criar com invariantes estritos e sem novas
        // operações fallíveis entre aqui e SaveChangesAsync.
        var numeroResult = await _sequenceManager.GetNextNumeroAsync(
            command.TenantId, tenant.ConfiguracaoFiscal.Serie, cancellationToken);
        if (numeroResult.IsFailure)
            return Result.Failure<IssueDocumentResponse>(numeroResult.Error);

        // 5. Gerar cNF com RandomNumberGenerator (nunca Random.Shared)
        var cNFBytes = RandomNumberGenerator.GetBytes(4);
        var cNF = (BitConverter.ToUInt32(cNFBytes, 0) % 100_000_000).ToString("D8");

        // 6. Montar ChaveAcesso
        var now = _timeProvider.GetUtcNow();
        var aamm = now.ToString("yyMM");
        var chaveResult = ChaveAcesso.Gerar(
            tenant.ConfiguracaoFiscal.UfCodigo,
            aamm,
            tenant.Cnpj.Valor,
            (int)command.Tipo,
            tenant.ConfiguracaoFiscal.Serie,
            numeroResult.Value.ToString(),
            TipoEmissao.Normal,
            cNF);

        if (chaveResult.IsFailure)
            return Result.Failure<IssueDocumentResponse>(chaveResult.Error);

        // 7. Criar DocumentoFiscal
        var documentoResult = DocumentoFiscal.Criar(
            new DocumentoFiscalId(Guid.CreateVersion7()),
            command.TenantId,
            command.ClienteAppId,
            command.IdempotencyKey,
            command.Tipo,
            chaveResult.Value,
            numeroResult.Value,
            tenant.ConfiguracaoFiscal.Serie,
            command.IndPresenca,
            itemsResult.Value,
            pagamentosResult.Value,
            _timeProvider);

        if (documentoResult.IsFailure)
            return Result.Failure<IssueDocumentResponse>(documentoResult.Error);

        var documento = documentoResult.Value;

        // 8. Persistir Criado + transicionar para Enfileirado na mesma transação
        var enfileirarResult = documento.Enfileirar();
        if (enfileirarResult.IsFailure)
            return Result.Failure<IssueDocumentResponse>(enfileirarResult.Error);

        await _documentoRepo.AddAsync(documento, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // 9. Enfileirar job APÓS commit — se falhar, ReconciliacaoJobProcessor reprocessa Enfileirado antigo
        await _jobQueue.EnqueueProcessingAsync(documento.Id, cancellationToken);

        return Result.Success(MapToResponse(documento));
    }

    private static Result<List<ItemDocumento>> BuildItems(IReadOnlyList<ItemDocumentoDto> dtos)
    {
        var items = new List<ItemDocumento>(dtos.Count);

        for (var i = 0; i < dtos.Count; i++)
        {
            var dto = dtos[i];

            var tributoResult = Tributo.Criar(
                dto.Tributo.TipoIcms,
                dto.Tributo.CsosnOuCst,
                dto.Tributo.AliquotaIcms,
                dto.Tributo.BaseCalculoIcms,
                dto.Tributo.ValorIcms,
                dto.Tributo.CstPis,
                dto.Tributo.BaseCalculoPis,
                dto.Tributo.AliquotaPis,
                dto.Tributo.ValorPis,
                dto.Tributo.CstCofins,
                dto.Tributo.BaseCalculoCofins,
                dto.Tributo.AliquotaCofins,
                dto.Tributo.ValorCofins);

            if (tributoResult.IsFailure)
                return Result.Failure<List<ItemDocumento>>(tributoResult.Error);

            var produtoResult = Produto.Criar(
                dto.CodigoProduto,
                dto.Descricao,
                dto.Ncm,
                dto.Cest,
                dto.CfopSaida,
                dto.UnidadeComercial,
                dto.Quantidade,
                dto.ValorUnitario,
                dto.ValorDesconto,
                dto.OrigemMercadoria);

            if (produtoResult.IsFailure)
                return Result.Failure<List<ItemDocumento>>(produtoResult.Error);

            items.Add(new ItemDocumento(i + 1, produtoResult.Value, tributoResult.Value));
        }

        return Result.Success(items);
    }

    private static Result<List<Pagamento>> BuildPagamentos(IReadOnlyList<PagamentoDto> dtos)
    {
        var pagamentos = new List<Pagamento>(dtos.Count);

        foreach (var dto in dtos)
        {
            var result = Pagamento.Criar(dto.TipoPagamento, dto.Valor);
            if (result.IsFailure)
                return Result.Failure<List<Pagamento>>(result.Error);

            pagamentos.Add(result.Value);
        }

        return Result.Success(pagamentos);
    }

    private static IssueDocumentResponse MapToResponse(DocumentoFiscal doc) =>
        new(
            doc.Id,
            doc.Status,
            doc.ChaveAcesso?.Valor,
            $"/api/v1/documentos/{doc.Id.Value}/status",
            doc.CreatedAt);
}
