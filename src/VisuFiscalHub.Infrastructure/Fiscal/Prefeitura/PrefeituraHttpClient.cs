using System.Net.Http.Headers;
using System.Text;

namespace VisuFiscalHub.Infrastructure.Fiscal.Prefeitura;

/// <summary>
/// Cliente HTTP para os webservices SOAP das prefeituras (padrão ABRASF v2.04).
/// Prefeituras geralmente não exigem mTLS — apenas HTTPS com assinatura XML.
/// </summary>
internal sealed class PrefeituraHttpClient
{
    private const string UserAgent = "VisuFiscalHub/1.0 NfseEmissor";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    public async Task<string> PostSoapAsync(
        string url,
        string soapEnvelope,
        CancellationToken ct)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler, disposeHandler: false);
        client.DefaultRequestHeaders.Add("User-Agent", UserAgent);

        using var content = new StringContent(soapEnvelope, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/soap+xml")
        {
            CharSet = "utf-8"
        };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(RequestTimeout);

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsync(url, content, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Timeout de {RequestTimeout.TotalSeconds}s atingido para {url}.");
        }

        // Prefeituras retornam HTTP 500 para erros SOAP — não usar EnsureSuccessStatusCode.
        return await response.Content.ReadAsStringAsync(ct);
    }
}
