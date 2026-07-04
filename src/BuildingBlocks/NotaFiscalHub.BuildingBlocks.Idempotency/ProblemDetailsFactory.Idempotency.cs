using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace NotaFiscalHub.BuildingBlocks.Idempotency;

/// <summary>
/// Fábrica dos <see cref="ProblemDetails"/> (RFC 7807) específicos do middleware de Idempotency-Key (spec
/// B4 §Abordagem passo 3). Nome do arquivo em estilo partial (<c>ProblemDetailsFactory.Idempotency.cs</c>)
/// para sinalizar que esta é a fatia do kernel dedicada a Idempotency — não há uma
/// <c>ProblemDetailsFactory</c> compartilhada ainda no projeto (Tarefa 4 é a primeira a introduzir RFC
/// 7807 neste código); um helper comum entre middlewares do kernel pode ser extraído quando um segundo
/// consumidor aparecer.
/// </summary>
internal static class IdempotencyProblemDetailsFactory
{
    private const string TipoBase = "https://notafiscalhub.dev/problems/";

    public static ProblemDetails KeyObrigatoria() => new()
    {
        Type = "idempotency_key_obrigatoria",
        Title = "Idempotency-Key obrigatória",
        Status = StatusCodes.Status400BadRequest,
        Detail = "Esta rota exige o header Idempotency-Key.",
    };

    public static ProblemDetails KeyInvalida() => new()
    {
        Type = "idempotency_key_invalida",
        Title = "Idempotency-Key inválida",
        Status = StatusCodes.Status400BadRequest,
        Detail = "O header Idempotency-Key deve ter entre 1 e 255 caracteres visíveis.",
    };

    /// <summary>Não revela o payload original (spec B4 §Abordagem passo 3) — só informa que houve conflito.</summary>
    public static ProblemDetails KeyConflito(HttpContext context)
    {
        var problem = new ProblemDetails
        {
            Type = "idempotency_key_conflito",
            Title = "Idempotency-Key reutilizada com payload diferente",
            Status = StatusCodes.Status409Conflict,
            Detail = "Esta Idempotency-Key já foi usada com um corpo de requisição diferente.",
        };
        problem.Extensions["traceId"] = TraceIdAtual(context);
        return problem;
    }

    public static ProblemDetails RequisicaoEmProcessamento(HttpContext context)
    {
        var problem = new ProblemDetails
        {
            Type = "requisicao_em_processamento",
            Title = "Requisição com esta Idempotency-Key já está em processamento",
            Status = StatusCodes.Status409Conflict,
            Detail = "Outra requisição com a mesma Idempotency-Key está em processamento. Tente novamente em instantes.",
        };
        problem.Extensions["traceId"] = TraceIdAtual(context);
        return problem;
    }

    private static string TraceIdAtual(HttpContext context) =>
        Activity.Current?.Id ?? context.TraceIdentifier;
}
