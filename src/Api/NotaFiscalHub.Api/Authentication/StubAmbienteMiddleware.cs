namespace NotaFiscalHub.Api.Authentication;

/// <summary>
/// Resolução interina do "ambiente da credencial" (spec B4 §Abordagem passo 6), válida SOMENTE em
/// Development/testes de integração: lê o header <c>X-Nfh-Ambiente</c> (valores esperados:
/// <c>producao</c>/<c>homologacao</c>) e publica em <c>HttpContext.Items["IdempotencyAmbiente"]</c>, que é
/// o que <c>IdempotencyEndpointFilter</c> lê. Ausente o header, assume <c>producao</c> (mesmo default do
/// filter). A Fase 1 substitui isso pela derivação real a partir do prefixo da API key
/// (<c>nfh_test_</c>/<c>nfh_live_</c>) sem alterar o contrato consumido pelo filter.
/// </summary>
public sealed class StubAmbienteMiddleware(RequestDelegate next)
{
    public const string HeaderAmbiente = "X-Nfh-Ambiente";
    public const string ItemKey = "IdempotencyAmbiente";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderAmbiente, out var valores) && valores.ToString() is { Length: > 0 } ambiente)
        {
            context.Items[ItemKey] = ambiente;
        }

        await next(context);
    }
}
