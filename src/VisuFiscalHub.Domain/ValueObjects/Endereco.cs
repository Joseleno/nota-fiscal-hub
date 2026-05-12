using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Errors;

namespace VisuFiscalHub.Domain.ValueObjects;

public sealed record Endereco(
    string Logradouro,
    string Numero,
    string? Complemento,
    string Bairro,
    string Municipio,
    int CodigoMunicipio,
    string Uf,
    string Cep,
    string CodigoPais,
    string? Telefone)
{
    public static Result<Endereco> Criar(
        string logradouro,
        string numero,
        string? complemento,
        string bairro,
        string municipio,
        int codigoMunicipio,
        string uf,
        string cep,
        string codigoPais = "1058",
        string? telefone = null)
    {
        if (string.IsNullOrWhiteSpace(logradouro))
            return Result.Failure<Endereco>(TenantErrors.EnderecoInvalido);

        if (string.IsNullOrWhiteSpace(numero))
            return Result.Failure<Endereco>(TenantErrors.EnderecoInvalido);

        if (string.IsNullOrWhiteSpace(bairro))
            return Result.Failure<Endereco>(TenantErrors.EnderecoInvalido);

        if (string.IsNullOrWhiteSpace(municipio))
            return Result.Failure<Endereco>(TenantErrors.EnderecoInvalido);

        if (codigoMunicipio < 1000000 || codigoMunicipio > 9999999)
            return Result.Failure<Endereco>(TenantErrors.EnderecoInvalido);

        if (string.IsNullOrWhiteSpace(uf) || uf.Length != 2 || !uf.All(char.IsLetter))
            return Result.Failure<Endereco>(TenantErrors.EnderecoInvalido);

        var cepNormalizado = cep?.Replace("-", string.Empty).Trim() ?? string.Empty;
        if (cepNormalizado.Length != 8 || !cepNormalizado.All(char.IsDigit))
            return Result.Failure<Endereco>(TenantErrors.EnderecoInvalido);

        return Result.Success(new Endereco(
            logradouro.Trim(),
            numero.Trim(),
            complemento?.Trim(),
            bairro.Trim(),
            municipio.Trim(),
            codigoMunicipio,
            uf.ToUpperInvariant(),
            cepNormalizado,
            codigoPais,
            telefone?.Trim()));
    }
}
