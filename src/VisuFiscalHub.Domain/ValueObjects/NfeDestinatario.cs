using System.Text.RegularExpressions;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Errors;

namespace VisuFiscalHub.Domain.ValueObjects;

public sealed partial record NfeDestinatario(
    string CnpjOuCpf,
    string RazaoSocial,
    int IndIeDest,
    string? Ie,
    string Logradouro,
    string Numero,
    string? Complemento,
    string Bairro,
    string Municipio,
    string CodigoMunicipio,
    string Uf,
    string Cep,
    string? Email)
{
    [GeneratedRegex(@"^\d{8}$")]
    private static partial Regex CepRegex();

    [GeneratedRegex(@"^\d{7}$")]
    private static partial Regex CodigoMunicipioRegex();

    private static readonly int[] IndIeDestValidos = [1, 2, 9];

    public static Result<NfeDestinatario> Criar(
        string cnpjOuCpf,
        string razaoSocial,
        int indIeDest,
        string? ie,
        string logradouro,
        string numero,
        string? complemento,
        string bairro,
        string municipio,
        string codigoMunicipio,
        string uf,
        string cep,
        string? email)
    {
        if (string.IsNullOrWhiteSpace(cnpjOuCpf)
            || (cnpjOuCpf.Length != 11 && cnpjOuCpf.Length != 14)
            || !cnpjOuCpf.All(char.IsDigit))
            return Result.Failure<NfeDestinatario>(DocumentoFiscalErrors.NfeDestinatarioInvalido);

        if (string.IsNullOrWhiteSpace(razaoSocial) || razaoSocial.Length > 60)
            return Result.Failure<NfeDestinatario>(DocumentoFiscalErrors.NfeDestinatarioInvalido);

        if (!IndIeDestValidos.Contains(indIeDest))
            return Result.Failure<NfeDestinatario>(DocumentoFiscalErrors.NfeDestinatarioInvalido);

        if (indIeDest == 1 && string.IsNullOrWhiteSpace(ie))
            return Result.Failure<NfeDestinatario>(DocumentoFiscalErrors.NfeDestinatarioInvalido);

        if (string.IsNullOrWhiteSpace(logradouro) || logradouro.Length > 60)
            return Result.Failure<NfeDestinatario>(DocumentoFiscalErrors.NfeDestinatarioInvalido);

        if (string.IsNullOrWhiteSpace(numero) || numero.Length > 60)
            return Result.Failure<NfeDestinatario>(DocumentoFiscalErrors.NfeDestinatarioInvalido);

        if (string.IsNullOrWhiteSpace(bairro) || bairro.Length > 60)
            return Result.Failure<NfeDestinatario>(DocumentoFiscalErrors.NfeDestinatarioInvalido);

        if (string.IsNullOrWhiteSpace(municipio) || municipio.Length > 60)
            return Result.Failure<NfeDestinatario>(DocumentoFiscalErrors.NfeDestinatarioInvalido);

        if (string.IsNullOrWhiteSpace(codigoMunicipio) || !CodigoMunicipioRegex().IsMatch(codigoMunicipio))
            return Result.Failure<NfeDestinatario>(DocumentoFiscalErrors.NfeDestinatarioInvalido);

        if (string.IsNullOrWhiteSpace(uf) || uf.Length != 2)
            return Result.Failure<NfeDestinatario>(DocumentoFiscalErrors.NfeDestinatarioInvalido);

        if (string.IsNullOrWhiteSpace(cep) || !CepRegex().IsMatch(cep))
            return Result.Failure<NfeDestinatario>(DocumentoFiscalErrors.NfeDestinatarioInvalido);

        var ieNorm    = string.IsNullOrWhiteSpace(ie)    ? null : ie.Trim();
        var emailNorm = string.IsNullOrWhiteSpace(email) ? null : email.Trim();

        return Result.Success(new NfeDestinatario(
            cnpjOuCpf, razaoSocial.Trim(), indIeDest, ieNorm,
            logradouro.Trim(), numero.Trim(), complemento?.Trim(),
            bairro.Trim(), municipio.Trim(), codigoMunicipio, uf.ToUpperInvariant(),
            cep, emailNorm));
    }
}
