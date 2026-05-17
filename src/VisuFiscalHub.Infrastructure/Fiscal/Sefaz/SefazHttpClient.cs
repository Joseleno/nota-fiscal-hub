using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace VisuFiscalHub.Infrastructure.Fiscal.Sefaz;

/// <summary>
/// Cliente HTTP para os webservices SOAP da SEFAZ com mTLS por Tenant.
/// Usa IHttpClientFactory para configuração base (User-Agent) e cria um handler
/// por chamada exclusivamente para injetar o certificado do Tenant — padrão necessário
/// quando o certificado muda por tenant e não pode ser fixo no factory registration.
/// O overhead de TCP/TLS handshake é aceitável: NFC-e não é high-throughput por design
/// (volume máximo típico: 300 NFC-e/hora por Tenant).
/// </summary>
internal sealed class SefazHttpClient(IHttpClientFactory httpClientFactory)
{
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

        using var mtlsClient = new HttpClient(handler, disposeHandler: false)
        {
            Timeout = RequestTimeout
        };

        // Copia headers base configurados no factory (User-Agent, etc.).
        var baseClient = httpClientFactory.CreateClient("sefaz-base");
        foreach (var header in baseClient.DefaultRequestHeaders)
            mtlsClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);

        using var content = new StringContent(soapEnvelope, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/soap+xml")
        {
            CharSet = "utf-8"
        };

        // Timeout vinculado ao ct do caller para separar timeout SEFAZ do token Hangfire.
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
