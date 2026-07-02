using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace VisuFiscalHub.Infrastructure.Fiscal.Sefaz;

/// <summary>
/// Cliente HTTP para os webservices SOAP da SEFAZ com mTLS por Tenant.
/// Cria um HttpClientHandler por chamada exclusivamente para injetar o certificado do Tenant —
/// padrão necessário quando o certificado muda por tenant e não pode ser fixo no factory.
/// O overhead de TCP/TLS handshake é aceitável: NFC-e não é high-throughput por design
/// (volume máximo típico: 300 NFC-e/hora por Tenant).
/// </summary>
internal sealed class SefazHttpClient
{
    // Sincronizado com o registro em DependencyInjection.cs ("sefaz-base" User-Agent).
    private const string UserAgent = "VisuFiscalHub/1.0 NfceEmissor";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    public async Task<string> PostSoapAsync(
        string url,
        string soapEnvelope,
        X509Certificate2 certificate,
        CancellationToken ct)
    {
        // Handler criado por chamada exclusivamente para mTLS — padrão reconhecido
        // quando o certificado varia por tenant e IHttpClientFactory não suporta cert dinâmico.
        // AllowAutoRedirect = false: previne bypass de SSRF e comportamento SOAP inesperado.
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            ClientCertificateOptions = ClientCertificateOption.Manual,
        };
        handler.ClientCertificates.Add(certificate);

        // Timeout gerenciado exclusivamente pelo timeoutCts abaixo — não definir Timeout no client
        // para evitar corrida entre dois mecanismos independentes com o mesmo valor.
        using var mtlsClient = new HttpClient(handler, disposeHandler: false);
        mtlsClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);

        using var content = new StringContent(soapEnvelope, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/soap+xml")
        {
            CharSet = "utf-8"
        };

        // CTS vinculado ao ct do caller: distingue timeout SEFAZ (30s) de cancelamento do Hangfire.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(RequestTimeout);

        HttpResponseMessage response;
        try
        {
            response = await mtlsClient.PostAsync(url, content, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Timeout de {RequestTimeout.TotalSeconds}s atingido para {url}.");
        }

        // NÃO usar EnsureSuccessStatusCode — SEFAZ retorna HTTP 500 para rejeições SOAP válidas.
        // O body SOAP é sempre lido e parseado independentemente do HTTP status.
        return await response.Content.ReadAsStringAsync(ct);
    }
}
