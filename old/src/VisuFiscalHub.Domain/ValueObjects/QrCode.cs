using System.Security.Cryptography;
using System.Text;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Errors;

namespace VisuFiscalHub.Domain.ValueObjects;

public sealed record QrCode
{
    public string UrlCompleta { get; }

    private QrCode(string urlCompleta)
    {
        UrlCompleta = urlCompleta;
    }

    /// <summary>
    /// Gera a URL do QR Code conforme NT 2019.001 v1.50.
    /// Fórmula do hash: SHA1(chaveAcesso + "|2|" + (int)ambiente + "|" + csc)
    /// URL: {urlConsultaSefaz}?p={chaveAcesso}|2|{(int)ambiente}|{cHashQRCode}
    /// O "|2|" é a versão do QR Code (literal fixo) — NÃO é tpAmb.
    /// O CSC nunca aparece na URL final.
    /// </summary>
    public static Result<QrCode> Gerar(
        ChaveAcesso chaveAcesso,
        AmbienteSefaz ambiente,
        string csc,
        string urlConsultaSefaz)
    {
        if (string.IsNullOrWhiteSpace(csc))
            return Result.Failure<QrCode>(DocumentoFiscalErrors.QrCodeInvalido);

        if (string.IsNullOrWhiteSpace(urlConsultaSefaz))
            return Result.Failure<QrCode>(DocumentoFiscalErrors.QrCodeInvalido);

        var tpAmb = ((int)ambiente).ToString();

        // SHA1(chaveAcesso + "|2|" + tpAmb + "|" + csc)
        // O "|2|" é a versão do QR Code — valor literal fixo, não é tpAmb
        var stringParaHash = chaveAcesso.Valor + "|2|" + tpAmb + "|" + csc;
        var hashBytes = SHA1.HashData(Encoding.UTF8.GetBytes(stringParaHash));
        var cHashQRCode = Convert.ToHexStringLower(hashBytes);

        var urlBase = urlConsultaSefaz.TrimEnd('/');
        var urlCompleta = $"{urlBase}?p={chaveAcesso.Valor}|2|{tpAmb}|{cHashQRCode}";

        return Result.Success(new QrCode(urlCompleta));
    }

    // Para reconstituição a partir do banco de dados — bypassa validação.
    public static QrCode FromStorage(string urlCompleta)
    {
        ArgumentNullException.ThrowIfNull(urlCompleta);
        return new(urlCompleta);
    }

    // Sentinel for document types that don't use QR codes (NF-e Modelo 55).
    public static QrCode NaoAplicavel() => new(string.Empty);

    public override string ToString() => UrlCompleta;
}
