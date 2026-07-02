using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Errors;

namespace VisuFiscalHub.Domain.ValueObjects;

public sealed record ServicoNfse(
    string CodigoServico,
    string Discriminacao,
    string? CodigoTributacaoMunicipio,
    decimal AliquotaIss,
    decimal BaseCalculoIss,
    decimal ValorIss,
    decimal? ValorDeducoes,
    bool IssRetido)
{
    public static Result<ServicoNfse> Criar(
        string codigoServico,
        string discriminacao,
        string? codigoTributacaoMunicipio,
        decimal aliquotaIss,
        decimal baseCalculoIss,
        decimal valorIss,
        decimal? valorDeducoes,
        bool issRetido)
    {
        if (string.IsNullOrWhiteSpace(codigoServico) || codigoServico.Length > 20)
            return Result.Failure<ServicoNfse>(DocumentoFiscalErrors.ServicoNfseInvalido);

        if (string.IsNullOrWhiteSpace(discriminacao) || discriminacao.Length > 2000)
            return Result.Failure<ServicoNfse>(DocumentoFiscalErrors.ServicoNfseInvalido);

        if (aliquotaIss < 0 || aliquotaIss > 100)
            return Result.Failure<ServicoNfse>(DocumentoFiscalErrors.ServicoNfseInvalido);

        if (baseCalculoIss < 0)
            return Result.Failure<ServicoNfse>(DocumentoFiscalErrors.ServicoNfseInvalido);

        if (valorIss < 0)
            return Result.Failure<ServicoNfse>(DocumentoFiscalErrors.ServicoNfseInvalido);

        if (valorDeducoes.HasValue && valorDeducoes.Value < 0)
            return Result.Failure<ServicoNfse>(DocumentoFiscalErrors.ServicoNfseInvalido);

        return Result.Success(new ServicoNfse(
            codigoServico.Trim(),
            discriminacao.Trim(),
            string.IsNullOrWhiteSpace(codigoTributacaoMunicipio) ? null : codigoTributacaoMunicipio.Trim(),
            aliquotaIss,
            baseCalculoIss,
            valorIss,
            valorDeducoes,
            issRetido));
    }
}
