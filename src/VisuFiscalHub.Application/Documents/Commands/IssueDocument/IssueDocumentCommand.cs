using Mediator;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Application.Documents.Commands.IssueDocument;

public sealed record IssueDocumentCommand : ICommand<Result<IssueDocumentResponse>>
{
    public TenantId TenantId { get; init; }
    public ClienteAppId ClienteAppId { get; init; }
    public string IdempotencyKey { get; init; } = string.Empty;
    public TipoDocumento Tipo { get; init; } = TipoDocumento.NfCe;
    public IReadOnlyList<ItemDocumentoDto> Itens { get; init; } = [];
    public IReadOnlyList<PagamentoDto> Pagamentos { get; init; } = [];
    public ConsumidorDto? Consumidor { get; init; }
    public int IndPresenca { get; init; } = 1;
}

public sealed record ItemDocumentoDto(
    string CodigoProduto,
    string Descricao,
    string Ncm,
    string? Cest,
    string CfopSaida,
    string UnidadeComercial,
    decimal Quantidade,
    decimal ValorUnitario,
    decimal ValorDesconto,
    OrigemMercadoria OrigemMercadoria,
    TributoDto Tributo);

public sealed record TributoDto(
    TipoIcms TipoIcms,
    int CsosnOuCst,
    decimal AliquotaIcms,
    decimal BaseCalculoIcms,
    decimal ValorIcms,
    CstPisCofins CstPis,
    decimal BaseCalculoPis,
    decimal AliquotaPis,
    decimal ValorPis,
    CstPisCofins CstCofins,
    decimal BaseCalculoCofins,
    decimal AliquotaCofins,
    decimal ValorCofins);

public sealed record PagamentoDto(TipoPagamento TipoPagamento, decimal Valor);

public sealed record ConsumidorDto(string? Cpf, string? Nome);
