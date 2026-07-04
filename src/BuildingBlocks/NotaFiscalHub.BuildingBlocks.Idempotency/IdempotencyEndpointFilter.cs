using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;

namespace NotaFiscalHub.BuildingBlocks.Idempotency;

/// <summary>
/// Endpoint filter de Idempotency-Key (spec B4 §Abordagem passo 3). Roda para endpoints que carregam
/// <see cref="IdempotencyKeyRouteMetadata"/> (via <c>RequireIdempotencyKey()</c>/<c>AcceptIdempotencyKey()</c>)
/// — é no-op para qualquer outro endpoint, inclusive GET/DELETE, mesmo que o header seja enviado (a
/// ausência do metadado é o único sinal que importa, não o verbo em si, mas na prática só rotas de
/// escrita registram o metadado).
///
/// Fluxo: valida a key (400 se ausente-quando-obrigatória ou inválida) → lê o corpo bufferizado → calcula
/// SHA-256 → <see cref="IIdempotencyStore.BeginAsync"/> → conforme o desfecho, executa o handler (e
/// completa/libera o registro) ou devolve replay/409 sem tocar o handler.
/// </summary>
public sealed class IdempotencyEndpointFilter(IIdempotencyStore store, ITenantContext tenantContext, IOptions<IdempotencyOptions> opcoes)
    : IEndpointFilter
{
    private const string HeaderKey = "Idempotency-Key";
    private const string HeaderReplayed = "Idempotency-Replayed";
    private readonly IdempotencyOptions _opcoes = opcoes.Value;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var metadata = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<IdempotencyKeyRouteMetadata>();
        if (metadata is null)
            return await next(context);

        var http = context.HttpContext;
        var temKey = http.Request.Headers.TryGetValue(HeaderKey, out StringValues valores);
        var key = valores.ToString();

        if (!temKey || string.IsNullOrEmpty(key))
        {
            if (metadata.Modo == IdempotencyKeyMode.Obrigatoria)
                return EscreverProblem(http, IdempotencyProblemDetailsFactory.KeyObrigatoria());

            // AcceptIdempotencyKey() sem header: processa normalmente, sem passar pelo store.
            return await next(context);
        }

        if (!IdempotencyKeyValidator.EhValida(key))
            return EscreverProblem(http, IdempotencyProblemDetailsFactory.KeyInvalida());

        var rota = RotaTemplate(http);
        var ambiente = AmbienteAtual(http);
        var contaId = tenantContext.ContaId;

        http.Request.EnableBuffering();
        var corpoCru = await LerCorpoCruAsync(http.Request, context.HttpContext.RequestAborted);
        http.Request.Body.Position = 0;

        var hash = PayloadHasher.Sha256(corpoCru);

        // CriadaEm/ExpiraEm aqui são placeholders: IdempotencyStore.BeginAsync recalcula os dois a partir
        // do seu próprio TimeProvider injetado (nunca de DateTimeOffset.UtcNow do filter) — é isso que
        // torna o relógio determinístico/injetável nos testes de expiração e órfão (spec B4 §Abordagem
        // passos 4/5). O filter não precisa (e não deve) ler o relógio para este propósito.
        var novo = new IdempotencyRecord(
            contaId, ambiente, key, rota, hash,
            IdempotencyState.EmProcessamento, null, null, null, null,
            CriadaEm: default, ExpiraEm: default);

        var resultado = await store.BeginAsync(novo, http.RequestAborted);

        switch (resultado.Outcome)
        {
            case IdempotencyBeginOutcome.ReplayConcluida:
                return EscreverReplay(http, resultado.Existente!);

            case IdempotencyBeginOutcome.ConflitoHashDiferente:
                return EscreverProblem(http, IdempotencyProblemDetailsFactory.KeyConflito(http));

            case IdempotencyBeginOutcome.CorridaEmProcessamento:
                http.Response.Headers.RetryAfter = _opcoes.RetryAfterSegundos.ToString();
                return EscreverProblem(http, IdempotencyProblemDetailsFactory.RequisicaoEmProcessamento(http));
        }

        return await ExecutarHandlerECompletarAsync(context, next, contaId, ambiente, rota, key);
    }

    /// <summary>
    /// Executa o handler capturando a resposta. IMPORTANTE sobre o pipeline de filtros do minimal API: o
    /// framework chama <see cref="IResult.ExecuteAsync"/> exatamente UMA VEZ, sobre o valor retornado pelo
    /// filtro MAIS EXTERNO — chamar <c>next(context)</c> não executa o <see cref="IResult"/> devolvido
    /// pelo handler, apenas o repassa. Por isso este método executa o <see cref="IResult"/> ele mesmo
    /// (para poder inspecionar/armazenar o corpo produzido) e devolve <see cref="Results.Empty"/> —
    /// "quando executado, não faz nada" — para que a segunda execução feita pelo framework seja inócua em
    /// vez de reescrever a resposta (ou lançar por já ter iniciado o response) sobre o que já foi
    /// gravado no stream real por este método.
    /// </summary>
    private async Task<object?> ExecutarHandlerECompletarAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next,
        Guid contaId, string ambiente, string rota, string key)
    {
        var http = context.HttpContext;
        var streamOriginal = http.Response.Body;
        await using var buffer = new MemoryStream();
        http.Response.Body = buffer;

        try
        {
            var resultadoDoHandler = await next(context);

            if (resultadoDoHandler is IResult ir)
            {
                await ir.ExecuteAsync(http);
            }
            else if (resultadoDoHandler is not null)
            {
                // Handler retornou um valor "cru" (string/objeto) em vez de IResult — o framework, se
                // fosse ele a lidar com isso, serializaria como texto/JSON. Como aqui já assumimos o
                // controle da escrita da resposta, replicamos a mesma regra (spec: comportamento-padrão
                // do minimal API para valores não-IResult) para não perder esse caminho de retorno.
                await Results.Json(resultadoDoHandler).ExecuteAsync(http);
            }

            buffer.Position = 0;
            var corpoBytes = buffer.ToArray();
            var status = http.Response.StatusCode;

            http.Response.Body = streamOriginal;
            await streamOriginal.WriteAsync(corpoBytes);

            if (status < 500)
            {
                var contentType = http.Response.ContentType ?? "application/octet-stream";
                var location = http.Response.Headers.Location.ToString() is { Length: > 0 } loc ? loc : null;
                var corpoTexto = System.Text.Encoding.UTF8.GetString(corpoBytes);

                await store.CompleteAsync(contaId, ambiente, rota, key, status, corpoTexto, contentType, location, http.RequestAborted);
            }
            else
            {
                await store.ReleaseAsync(contaId, ambiente, rota, key, http.RequestAborted);
            }

            return Results.Empty;
        }
        catch
        {
            http.Response.Body = streamOriginal;
            await store.ReleaseAsync(contaId, ambiente, rota, key, http.RequestAborted);
            throw;
        }
    }

    private static object EscreverReplay(HttpContext http, IdempotencyRecord existente)
    {
        http.Response.Headers[HeaderReplayed] = "true";
        http.Response.StatusCode = existente.RespostaStatus ?? StatusCodes.Status200OK;

        if (!string.IsNullOrEmpty(existente.RespostaContentType))
            http.Response.ContentType = existente.RespostaContentType;

        if (!string.IsNullOrEmpty(existente.RespostaLocation))
            http.Response.Headers.Location = existente.RespostaLocation;

        return Results.Text(existente.RespostaCorpo ?? string.Empty, existente.RespostaContentType);
    }

    private static object EscreverProblem(HttpContext http, Microsoft.AspNetCore.Mvc.ProblemDetails problem) =>
        Results.Problem(
            detail: problem.Detail,
            statusCode: problem.Status,
            title: problem.Title,
            type: problem.Type,
            extensions: problem.Extensions);

    private static async Task<byte[]> LerCorpoCruAsync(HttpRequest request, CancellationToken ct)
    {
        request.Body.Position = 0;
        using var memoria = new MemoryStream();
        await request.Body.CopyToAsync(memoria, ct);
        return memoria.ToArray();
    }

    private static string RotaTemplate(HttpContext http)
    {
        var endpoint = http.GetEndpoint();
        var padrao = (endpoint as Microsoft.AspNetCore.Routing.RouteEndpoint)?.RoutePattern.RawText ?? http.Request.Path.Value ?? "";
        return $"{http.Request.Method} {padrao}";
    }

    /// <summary>
    /// Ambiente da credencial (spec B4 §Abordagem passo 6) — Fase 1 resolverá isso de verdade a partir do
    /// prefixo da API key (<c>nfh_test_</c>/<c>nfh_live_</c>). Nesta tarefa o valor só precisa estar
    /// disponível na borda como string simples; o host de teste injeta um valor fake via
    /// <c>HttpContext.Items</c> (ver ambientes distintos no critério de aceite 7/Step 10).
    /// </summary>
    private static string AmbienteAtual(HttpContext http) =>
        http.Items.TryGetValue("IdempotencyAmbiente", out var valor) && valor is string ambiente
            ? ambiente
            : "producao";
}
