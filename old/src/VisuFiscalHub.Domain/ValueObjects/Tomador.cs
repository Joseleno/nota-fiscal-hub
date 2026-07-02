using System.Text.RegularExpressions;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Errors;

namespace VisuFiscalHub.Domain.ValueObjects;

public sealed partial record Tomador(
    string CnpjOuCpf,
    string RazaoSocial,
    string Logradouro,
    string Numero,
    string? Complemento,
    string Bairro,
    string Municipio,
    string CodigoMunicipio,
    string Uf,
    string Cep,
    string? Email,
    string? InscricaoMunicipal)
{
    [GeneratedRegex(@"^\d{8}$")]
    private static partial Regex CepRegex();

    [GeneratedRegex(@"^\d{7}$")]
    private static partial Regex CodigoMunicipioRegex();

    public static Result<Tomador> Criar(
        string cnpjOuCpf,
        string razaoSocial,
        string logradouro,
        string numero,
        string? complemento,
        string bairro,
        string municipio,
        string codigoMunicipio,
        string uf,
        string cep,
        string? email,
        string? inscricaoMunicipal)
    {
        if (string.IsNullOrWhiteSpace(cnpjOuCpf)
            || (cnpjOuCpf.Length != 11 && cnpjOuCpf.Length != 14)
            || !cnpjOuCpf.All(char.IsDigit))
            return Result.Failure<Tomador>(DocumentoFiscalErrors.TomadorInvalido);

        // ABRASF v2.04: xNome do tomador pode ter até 115 chars (vs 60 do NF-e SEFAZ)
        if (string.IsNullOrWhiteSpace(razaoSocial) || razaoSocial.Length > 115)
            return Result.Failure<Tomador>(DocumentoFiscalErrors.TomadorInvalido);

        // ABRASF v2.04: xLgr do tomador pode ter até 125 chars (vs 60 do NF-e SEFAZ)
        if (string.IsNullOrWhiteSpace(logradouro) || logradouro.Length > 125)
            return Result.Failure<Tomador>(DocumentoFiscalErrors.TomadorInvalido);

        if (string.IsNullOrWhiteSpace(numero) || numero.Length > 10)
            return Result.Failure<Tomador>(DocumentoFiscalErrors.TomadorInvalido);

        if (string.IsNullOrWhiteSpace(bairro) || bairro.Length > 60)
            return Result.Failure<Tomador>(DocumentoFiscalErrors.TomadorInvalido);

        if (string.IsNullOrWhiteSpace(municipio) || municipio.Length > 60)
            return Result.Failure<Tomador>(DocumentoFiscalErrors.TomadorInvalido);

        if (string.IsNullOrWhiteSpace(codigoMunicipio) || !CodigoMunicipioRegex().IsMatch(codigoMunicipio))
            return Result.Failure<Tomador>(DocumentoFiscalErrors.TomadorInvalido);

        if (string.IsNullOrWhiteSpace(uf) || uf.Length != 2 || !uf.All(char.IsLetter))
            return Result.Failure<Tomador>(DocumentoFiscalErrors.TomadorInvalido);

        if (string.IsNullOrWhiteSpace(cep) || !CepRegex().IsMatch(cep))
            return Result.Failure<Tomador>(DocumentoFiscalErrors.TomadorInvalido);

        return Result.Success(new Tomador(
            cnpjOuCpf,
            razaoSocial.Trim(),
            logradouro.Trim(),
            numero.Trim(),
            complemento?.Trim(),
            bairro.Trim(),
            municipio.Trim(),
            codigoMunicipio,
            uf.ToUpperInvariant(),
            cep,
            string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            string.IsNullOrWhiteSpace(inscricaoMunicipal) ? null : inscricaoMunicipal.Trim()));
    }

    public static Tomador FromStorage(
        string cnpjOuCpf, string razaoSocial,
        string logradouro, string numero, string? complemento,
        string bairro, string municipio, string codigoMunicipio,
        string uf, string cep, string? email, string? inscricaoMunicipal)
        => new(cnpjOuCpf, razaoSocial, logradouro, numero, complemento,
               bairro, municipio, codigoMunicipio, uf, cep, email, inscricaoMunicipal);
}
