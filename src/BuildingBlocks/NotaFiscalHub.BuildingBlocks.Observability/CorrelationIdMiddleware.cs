using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace NotaFiscalHub.BuildingBlocks.Observability;

/// <summary>
/// Middleware de borda (spec B7 passo 3): lê <c>X-Correlation-Id</c> do request, valida o formato, e
/// garante que <see cref="ICorrelationContext"/>/<c>LogContext</c>/<c>Activity.Current</c> tenham um
/// valor não-vazio antes de qualquer código de aplicação rodar. Devolve o mesmo valor no header de
/// resposta — sempre, mesmo quando o valor foi gerado pelo middleware (nunca omitido).
///
/// Header ausente OU fora do formato <c>[A-Za-z0-9\-]{8,64}</c> gera um GUID novo em vez de confiar no
/// valor do cliente: um CorrelationId flui para logs/traces/outbox, e aceitar qualquer string arbitrária
/// do cliente (ex.: contendo caracteres de controle, ou um valor gigante) abriria uma superfície de
/// injeção/DoS de baixo custo nesses três destinos.
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate proximo, ILogger<CorrelationIdMiddleware> logger)
{
    public const string NomeDoHeader = "X-Correlation-Id";

    [GeneratedRegex(@"^[A-Za-z0-9\-]{8,64}$")]
    private static partial Regex FormatoValido();

    public async Task InvokeAsync(HttpContext context, CorrelationContext correlationContext)
    {
        var correlationId = ResolverCorrelationId(context);

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[NomeDoHeader] = correlationId;
            return Task.CompletedTask;
        });

        using var escopoDeCorrelacao = correlationContext.Definir(correlationId);
        using var escopoDeLog = LogContext.PushProperty("CorrelationId", correlationId);

        Activity.Current?.SetTag("correlation_id", correlationId);

        await proximo(context);
    }

    private string ResolverCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(NomeDoHeader, out var valores))
        {
            var candidato = valores.ToString();
            if (FormatoValido().IsMatch(candidato))
                return candidato;

            logger.LogWarning(
                "CorrelationIdInvalido: header {NomeDoHeader} recebido fora do formato esperado — gerando novo valor.",
                NomeDoHeader);
        }

        return Guid.NewGuid().ToString();
    }
}
