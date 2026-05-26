namespace VisuFiscalHub.Infrastructure.Services;

internal static class PemNormalizer
{
    // Chaves PEM vindas de variáveis de ambiente (arquivo .env, Docker) chegam com \n
    // ou \r\n como literais de dois/quatro caracteres — não como newlines reais (U+000A).
    // RSA.ImportFromPem exige LF real entre header, corpo base64 e footer.
    internal static string Normalize(string pem) =>
        pem.Replace("\\r\\n", "\n").Replace("\\r", "\n").Replace("\\n", "\n");
}
