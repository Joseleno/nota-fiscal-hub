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
            if (existente.ClienteAppId != command.ClienteAppId)
                return Result.Failure<IssueDocumentResponse>(TenantErrors.NaoPertenceAoClienteApp);

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

        // 3. Construir itens, pagamentos e destinatário antes de consumir a sequence
        var itemsResult = BuildItems(command.Itens);
        if (itemsResult.IsFailure)
            return Result.Failure<IssueDocumentResponse>(itemsResult.Error);

        var pagamentosResult = BuildPagamentos(command.Pagamentos);
        if (pagamentosResult.IsFailure)
            return Result.Failure<IssueDocumentResponse>(pagamentosResult.Error);

        Domain.ValueObjects.NfeDestinatario? nfeDestinatario = null;
        if (command.NfeDestinatario is { } destDto)
        {
            var destResult = Domain.ValueObjects.NfeDestinatario.Criar(
                destDto.CnpjOuCpf, destDto.RazaoSocial, destDto.IndIeDest, destDto.Ie,
                destDto.Logradouro, destDto.Numero, destDto.Complemento,
                destDto.Bairro, destDto.Municipio, destDto.CodigoMunicipio,
                destDto.Uf, destDto.Cep, destDto.Email);
            if (destResult.IsFailure)
                return Result.Failure<IssueDocumentResponse>(destResult.Error);
            nfeDestinatario = destResult.Value;
        }

        // 4. Obter próximo número da sequence (após validações — evita consumir número em caso de falha).
        // ATENÇÃO: sequences PostgreSQL são não-transacionais. A partir deste ponto, qualquer falha
        // (ChaveAcesso.Gerar, DocumentoFiscal.Criar) produz uma lacuna permanente na numeração.
        // Manter ChaveAcesso.Gerar e DocumentoFiscal.Criar com invariantes estritos e sem novas
        // operações fallíveis entre aqui e SaveChangesAsync.
        var serie = command.Tipo == TipoDocumento.NFe
            ? tenant.ConfiguracaoFiscal.SerieNfe ?? tenant.ConfiguracaoFiscal.Serie
            : tenant.ConfiguracaoFiscal.Serie;

        var numeroResult = await _sequenceManager.GetNextNumeroAsync(
            command.TenantId, serie, cancellationToken);
        if (numeroResult.IsFailure)
            return Result.Failure<IssueDocumentResponse>(numeroResult.Error);

        // 5. Gerar cNF com RandomNumberGenerator (nunca Random.Shared)
        var cNFBytes = RandomNumberGenerator.GetBytes(4);
        var cNF = (BitConverter.ToUInt32(cNFBytes, 0) % 100_000_000).ToString("D8");

        // 6. Montar ChaveAcesso (NFSe não possui chave de acesso SEFAZ)
        var now = _timeProvider.GetUtcNow();
        ChaveAcesso? chaveAcesso = null;
        if (command.Tipo != TipoDocumento.NFSe)
        {
            var aamm = now.ToString("yyMM");
            var chaveResult = ChaveAcesso.Gerar(
                tenant.ConfiguracaoFiscal.UfCodigo,
                aamm,
                tenant.Cnpj.Valor,
                (int)command.Tipo,
                serie,
                numeroResult.Value.ToString(),
                TipoEmissao.Normal,
                cNF);

            if (chaveResult.IsFailure)
                return Result.Failure<IssueDocumentResponse>(chaveResult.Error);

            chaveAcesso = chaveResult.Value;
        }

        // 7. Mapear Tomador e ServicoNfse (NFSe only)
        Domain.ValueObjects.Tomador? tomador = null;
        if (command.Tomador is { } tomadorDto)
        {
            var tomadorResult = Domain.ValueObjects.Tomador.Criar(
                tomadorDto.CnpjOuCpf,
                tomadorDto.RazaoSocial,
                tomadorDto.Logradouro,
                tomadorDto.Numero,
                tomadorDto.Complemento,
                tomadorDto.Bairro,
                tomadorDto.Municipio,
                tomadorDto.CodigoMunicipio,
                tomadorDto.Uf,
                tomadorDto.Cep,
                tomadorDto.Email,
                tomadorDto.InscricaoMunicipal);
            if (tomadorResult.IsFailure)
                return Result.Failure<IssueDocumentResponse>(tomadorResult.Error);
            tomador = tomadorResult.Value;
        }

        Domain.ValueObjects.ServicoNfse? servicoNfse = null;
        if (command.ServicoNfse is { } servicoDto)
        {
            var servicoResult = Domain.ValueObjects.ServicoNfse.Criar(
                servicoDto.CodigoServico,
                servicoDto.Discriminacao,
                servicoDto.CodigoTributacaoMunicipio,
                servicoDto.AliquotaIss,
                servicoDto.BaseCalculoIss,
                servicoDto.ValorIss,
                servicoDto.ValorDeducoes,
                servicoDto.IssRetido);
            if (servicoResult.IsFailure)
                return Result.Failure<IssueDocumentResponse>(servicoResult.Error);
            servicoNfse = servicoResult.Value;
        }

        // 8. Criar DocumentoFiscal

        var documentoResult = DocumentoFiscal.Criar(
            new DocumentoFiscalId(Guid.CreateVersion7()),
            command.TenantId,
            command.ClienteAppId,
            command.IdempotencyKey,
            command.Tipo,
            chaveAcesso,
            numeroResult.Value,
            serie,
            command.IndPresenca,
            itemsResult.Value,
            pagamentosResult.Value,
            _timeProvider,
            command.Consumidor?.Cpf,
            command.Consumidor?.Nome,
            nfeDestinatario,
            command.NatOp,
            command.ModFrete,
            tomador,
            servicoNfse);

        if (documentoResult.IsFailure)
            return Result.Failure<IssueDocumentResponse>(documentoResult.Error);

        var documento = documentoResult.Value;

        // 9. Persistir Criado + transicionar para Enfileirado na mesma transação
        var enfileirarResult = documento.Enfileirar();
        if (enfileirarResult.IsFailure)
            return Result.Failure<IssueDocumentResponse>(enfileirarResult.Error);

        await _documentoRepo.AddAsync(documento, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // 10. Enfileirar job APÓS commit — se falhar, ReconciliacaoJobProcessor reprocessa Enfileirado antigo
        await _jobQueue.EnqueueProcessingAsync(documento.Id, documento.Tipo, cancellationToken);

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
