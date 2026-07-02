using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Errors;

namespace VisuFiscalHub.Domain.ValueObjects;

public sealed record Cpf
{
    public string Valor { get; }

    private Cpf(string valor)
    {
        Valor = valor;
    }

    public static Result<Cpf> Criar(string cpf)
    {
        if (string.IsNullOrWhiteSpace(cpf))
            return Result.Failure<Cpf>(DocumentoFiscalErrors.CpfInvalido);

        var normalizado = Normalizar(cpf);

        if (normalizado.Length != 11)
            return Result.Failure<Cpf>(DocumentoFiscalErrors.CpfInvalido);

        if (!normalizado.All(char.IsDigit))
            return Result.Failure<Cpf>(DocumentoFiscalErrors.CpfInvalido);

        if (TodosDigitosIguais(normalizado))
            return Result.Failure<Cpf>(DocumentoFiscalErrors.CpfInvalido);

        if (!ValidarDigitosVerificadores(normalizado))
            return Result.Failure<Cpf>(DocumentoFiscalErrors.CpfInvalido);

        return Result.Success(new Cpf(normalizado));
    }

    private static string Normalizar(string cpf) =>
        cpf.Replace(".", string.Empty)
           .Replace("-", string.Empty)
           .Trim();

    private static bool TodosDigitosIguais(string cpf)
    {
        var primeiroDigito = cpf[0];
        return cpf.All(c => c == primeiroDigito);
    }

    private static bool ValidarDigitosVerificadores(string cpf)
    {
        // Primeiro DV: pesos 10,9,8,7,6,5,4,3,2 aplicados aos 9 primeiros dígitos
        var soma1 = 0;
        for (var i = 0; i < 9; i++)
            soma1 += (cpf[i] - '0') * (10 - i);

        var resto1 = (soma1 * 10) % 11;
        var dv1 = (resto1 == 10 || resto1 == 11) ? 0 : resto1;

        if ((cpf[9] - '0') != dv1)
            return false;

        // Segundo DV: pesos 11,10,9,8,7,6,5,4,3,2 aplicados aos 10 primeiros dígitos
        var soma2 = 0;
        for (var i = 0; i < 10; i++)
            soma2 += (cpf[i] - '0') * (11 - i);

        var resto2 = (soma2 * 10) % 11;
        var dv2 = (resto2 == 10 || resto2 == 11) ? 0 : resto2;

        return (cpf[10] - '0') == dv2;
    }

    // Para reconstituição a partir do banco de dados — bypassa validação.
    public static Cpf FromStorage(string valor) => new(valor.Trim());

    public override string ToString() => Valor;
}
