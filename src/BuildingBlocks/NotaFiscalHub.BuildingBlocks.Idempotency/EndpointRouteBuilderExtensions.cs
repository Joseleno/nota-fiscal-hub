using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace NotaFiscalHub.BuildingBlocks.Idempotency;

/// <summary>
/// Extensões opt-in de rota (spec B4 §Abordagem passo 3, §Interfaces): <c>RequireIdempotencyKey()</c> é o
/// caso de <c>POST /v1/nfce</c> (header obrigatório); <c>AcceptIdempotencyKey()</c> é para as demais
/// escritas do design §3.2 (processam normalmente sem o header, mas obtêm replay/conflito quando enviado).
/// Ambas anexam <see cref="IdempotencyEndpointFilter"/> ao pipeline do endpoint.
/// </summary>
public static class EndpointRouteBuilderExtensions
{
    public static RouteHandlerBuilder RequireIdempotencyKey(this RouteHandlerBuilder builder) =>
        builder
            .WithMetadata(new IdempotencyKeyRouteMetadata(IdempotencyKeyMode.Obrigatoria))
            .AddEndpointFilter<IdempotencyEndpointFilter>();

    public static RouteHandlerBuilder AcceptIdempotencyKey(this RouteHandlerBuilder builder) =>
        builder
            .WithMetadata(new IdempotencyKeyRouteMetadata(IdempotencyKeyMode.Opcional))
            .AddEndpointFilter<IdempotencyEndpointFilter>();
}
