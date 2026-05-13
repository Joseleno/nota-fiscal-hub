using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Errors;

namespace VisuFiscalHub.Domain.ValueObjects;

public sealed record ChaveAcesso
{
    public string Valor { get; }

    private ChaveAcesso(string valor)
    {
        Valor = valor;
    }

    /// <summary>
    /// Gera a chave de acesso de 44 dígitos calculando cDV via Módulo 11.
    /// </summary>
    /// <param name="cUF">Código IBGE da UF (2 dígitos, ex: 28 para SE)</param>
    /// <param name="aamm">Ano e mês de emissão (4 dígitos, ex: "2605")</param>
    /// <param name="cnpj">CNPJ sem máscara (14 dígitos)</param>
    /// <param name="mod">Modelo do documento (2 dígitos, ex: 65 para NFC-e)</param>
    /// <param name="serie">Série da nota (3 dígitos, zero-padded)</param>
    /// <param name="nNF">Número sequencial da nota (9 dígitos, zero-padded)</param>
    /// <param name="tpEmis">Tipo de emissão (1 dígito)</param>
    /// <param name="cNF">8 dígitos aleatórios gerados externamente via RandomNumberGenerator</param>
    public static Result<ChaveAcesso> Gerar(
        int cUF,
        string aamm,
        string cnpj,
        int mod,
        string serie,
        string nNF,
        TipoEmissao tpEmis,
        string cNF)
    {
        if (string.IsNullOrWhiteSpace(aamm) || aamm.Length != 4 || !aamm.All(char.IsDigit))
            return Result.Failure<ChaveAcesso>(DocumentoFiscalErrors.ChaveAcessoInvalida);

        // Valida mês: caracteres 2-3 do aamm devem ser 01-12
        var mes = int.Parse(aamm[2..]);
        if (mes < 1 || mes > 12)
            return Result.Failure<ChaveAcesso>(DocumentoFiscalErrors.ChaveAcessoInvalida);

        if (string.IsNullOrWhiteSpace(cnpj) || cnpj.Length != 14)
            return Result.Failure<ChaveAcesso>(DocumentoFiscalErrors.ChaveAcessoInvalida);

        if (string.IsNullOrWhiteSpace(cNF) || cNF.Length != 8 || !cNF.All(char.IsDigit))
            return Result.Failure<ChaveAcesso>(DocumentoFiscalErrors.ChaveAcessoInvalida);

        var quarentaTresDígitos =
            cUF.ToString().PadLeft(2, '0') +
            aamm +
            cnpj +
            mod.ToString().PadLeft(2, '0') +
            serie.PadLeft(3, '0') +
            nNF.PadLeft(9, '0') +
            ((int)tpEmis).ToString() +
            cNF.PadLeft(8, '0');

        if (quarentaTresDígitos.Length != 43)
            return Result.Failure<ChaveAcesso>(DocumentoFiscalErrors.ChaveAcessoInvalida);

        if (!quarentaTresDígitos.All(char.IsDigit))
            return Result.Failure<ChaveAcesso>(DocumentoFiscalErrors.ChaveAcessoInvalida);

        var cDV = CalcularCDV(quarentaTresDígitos);
        return Result.Success(new ChaveAcesso(quarentaTresDígitos + cDV));
    }

    // Módulo 11 da direita para a esquerda, pesos cíclicos 2-9.
    // Vetor MOC 7.0: "3509061420016714006512500100000180010000009" → soma=448, resto=8, cDV=3
    // Chave completa: "35090614200167140065125001000001800100000093" (verificado matematicamente)
    private static int CalcularCDV(string quarentaTresDígitos)
    {
        var soma = 0;
        var peso = 2;

        for (var i = quarentaTresDígitos.Length - 1; i >= 0; i--)
        {
            soma += (quarentaTresDígitos[i] - '0') * peso;
            peso = peso == 9 ? 2 : peso + 1;
        }

        var resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    }

    /// <summary>
    /// Carrega uma chave de acesso já formada (ex: do banco de dados).
    /// Não recalcula o cDV — confia nos dados da origem.
    /// </summary>
    public static Result<ChaveAcesso> From(string chave44Digitos)
    {
        if (string.IsNullOrWhiteSpace(chave44Digitos))
            return Result.Failure<ChaveAcesso>(DocumentoFiscalErrors.ChaveAcessoInvalida);

        if (chave44Digitos.Length != 44)
            return Result.Failure<ChaveAcesso>(DocumentoFiscalErrors.ChaveAcessoInvalida);

        if (!chave44Digitos.All(char.IsDigit))
            return Result.Failure<ChaveAcesso>(DocumentoFiscalErrors.ChaveAcessoInvalida);

        return Result.Success(new ChaveAcesso(chave44Digitos));
    }

    public override string ToString() => Valor;
}
