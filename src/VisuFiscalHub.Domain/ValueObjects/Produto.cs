using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Errors;

namespace VisuFiscalHub.Domain.ValueObjects;

public sealed record Produto(
    string CodigoProduto,
    string Descricao,
    string Ncm,
    string? Cest,
    string CfopSaida,
    string UnidadeComercial,
    decimal Quantidade,
    decimal ValorUnitario,
    decimal ValorDesconto,
    OrigemMercadoria OrigemMercadoria)
{
    public decimal ValorBruto => Quantidade * ValorUnitario;
    public decimal ValorLiquido => ValorBruto - ValorDesconto;

    public static Result<Produto> Criar(
        string codigoProduto,
        string descricao,
        string ncm,
        string? cest,
        string cfopSaida,
        string unidadeComercial,
        decimal quantidade,
        decimal valorUnitario,
        decimal valorDesconto,
        OrigemMercadoria origemMercadoria)
    {
        if (string.IsNullOrWhiteSpace(codigoProduto))
            return Result.Failure<Produto>(DocumentoFiscalErrors.ProdutoInvalido);

        if (string.IsNullOrWhiteSpace(descricao))
            return Result.Failure<Produto>(DocumentoFiscalErrors.ProdutoInvalido);

        if (string.IsNullOrWhiteSpace(ncm) || ncm.Length != 8 || !ncm.All(char.IsDigit))
            return Result.Failure<Produto>(DocumentoFiscalErrors.ProdutoInvalido);

        if (string.IsNullOrWhiteSpace(cfopSaida) || cfopSaida.Length != 4 || !cfopSaida.All(char.IsDigit))
            return Result.Failure<Produto>(DocumentoFiscalErrors.ProdutoInvalido);

        if (string.IsNullOrWhiteSpace(unidadeComercial))
            return Result.Failure<Produto>(DocumentoFiscalErrors.ProdutoInvalido);

        if (quantidade <= 0)
            return Result.Failure<Produto>(DocumentoFiscalErrors.ProdutoInvalido);

        if (valorUnitario <= 0)
            return Result.Failure<Produto>(DocumentoFiscalErrors.ProdutoInvalido);

        if (valorDesconto < 0)
            return Result.Failure<Produto>(DocumentoFiscalErrors.ProdutoInvalido);

        var valorBruto = quantidade * valorUnitario;
        if (valorDesconto >= valorBruto)
            return Result.Failure<Produto>(DocumentoFiscalErrors.ProdutoInvalido);

        return Result.Success(new Produto(
            codigoProduto,
            descricao,
            ncm,
            cest,
            cfopSaida,
            unidadeComercial,
            quantidade,
            valorUnitario,
            valorDesconto,
            origemMercadoria));
    }
}
