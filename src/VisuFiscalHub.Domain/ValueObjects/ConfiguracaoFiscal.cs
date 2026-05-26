using System.Text.RegularExpressions;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Errors;

namespace VisuFiscalHub.Domain.ValueObjects;

public sealed partial record ConfiguracaoFiscal(
    RegimeTributario Crt,
    string Serie,
    AmbienteSefaz Ambiente,
    int UfCodigo,
    string? InscricaoEstadual,
    string? SerieNfe = null,
    string? InscricaoMunicipal = null)
{
    // Série deve ser numérica de 1 a 3 dígitos — obrigatório para evitar SQL injection no DDL da sequence
    [GeneratedRegex(@"^[0-9]{1,3}$")]
    private static partial Regex SerieRegex();

    // Códigos IBGE válidos das 27 UFs brasileiras
    private static readonly int[] CodigosUfValidos =
    [
        11, 12, 13, 14, 15, 16, 17, 21, 22, 23, 24, 25, 26, 27, 28, 29,
        31, 32, 33, 35, 41, 42, 43, 50, 51, 52, 53
    ];

    public static Result<ConfiguracaoFiscal> Criar(
        RegimeTributario crt,
        string serie,
        AmbienteSefaz ambiente,
        int ufCodigo,
        string? inscricaoEstadual = null,
        string? serieNfe = null,
        string? inscricaoMunicipal = null)
    {
        if (string.IsNullOrWhiteSpace(serie) || !SerieRegex().IsMatch(serie))
            return Result.Failure<ConfiguracaoFiscal>(TenantErrors.ConfiguracaoFiscalInvalida);

        if (!CodigosUfValidos.Contains(ufCodigo))
            return Result.Failure<ConfiguracaoFiscal>(TenantErrors.ConfiguracaoFiscalInvalida);

        if (serieNfe is not null && !SerieRegex().IsMatch(serieNfe))
            return Result.Failure<ConfiguracaoFiscal>(TenantErrors.ConfiguracaoFiscalInvalida);

        var ie = string.IsNullOrWhiteSpace(inscricaoEstadual) ? null : inscricaoEstadual.Trim();
        var im = string.IsNullOrWhiteSpace(inscricaoMunicipal) ? null : inscricaoMunicipal.Trim();

        return Result.Success(new ConfiguracaoFiscal(crt, serie, ambiente, ufCodigo, ie, serieNfe, im));
    }
}
