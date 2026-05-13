using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Errors;

namespace VisuFiscalHub.Domain.ValueObjects;

public sealed record Cnpj
{
    public string Valor { get; }

    private Cnpj(string valor)
    {
        Valor = valor;
    }

    public static Result<Cnpj> Criar(string cnpj)
    {
        if (string.IsNullOrWhiteSpace(cnpj))
            return Result.Failure<Cnpj>(TenantErrors.CnpjInvalido);

        var normalizado = Normalizar(cnpj);

        if (normalizado.Length != 14)
            return Result.Failure<Cnpj>(TenantErrors.CnpjInvalido);

        if (!normalizado.All(char.IsDigit))
            return Result.Failure<Cnpj>(TenantErrors.CnpjInvalido);

        if (TodosDigitosIguais(normalizado))
            return Result.Failure<Cnpj>(TenantErrors.CnpjInvalido);

        if (!ValidarDigitosVerificadores(normalizado))
            return Result.Failure<Cnpj>(TenantErrors.CnpjInvalido);

        return Result.Success(new Cnpj(normalizado));
    }

    private static string Normalizar(string cnpj) =>
        cnpj.Replace(".", string.Empty)
            .Replace("/", string.Empty)
            .Replace("-", string.Empty)
            .Trim();

    private static bool TodosDigitosIguais(string cnpj)
    {
        var primeiroDigito = cnpj[0];
        return cnpj.All(c => c == primeiroDigito);
    }

    private static bool ValidarDigitosVerificadores(string cnpj)
    {
        // Primeiro DV: pesos 5,4,3,2,9,8,7,6,5,4,3,2 aplicados aos 12 primeiros dígitos
        int[] pesos1 = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        var soma1 = 0;
        for (var i = 0; i < 12; i++)
            soma1 += (cnpj[i] - '0') * pesos1[i];

        var resto1 = soma1 % 11;
        var dv1 = resto1 < 2 ? 0 : 11 - resto1;

        if ((cnpj[12] - '0') != dv1)
            return false;

        // Segundo DV: pesos 6,5,4,3,2,9,8,7,6,5,4,3,2 aplicados aos 13 primeiros dígitos
        int[] pesos2 = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        var soma2 = 0;
        for (var i = 0; i < 13; i++)
            soma2 += (cnpj[i] - '0') * pesos2[i];

        var resto2 = soma2 % 11;
        var dv2 = resto2 < 2 ? 0 : 11 - resto2;

        return (cnpj[13] - '0') == dv2;
    }

    // Para reconstituição a partir do banco de dados — bypassa validação.
    public static Cnpj FromStorage(string valor) => new(valor.Trim());

    public override string ToString() => Valor;
}
