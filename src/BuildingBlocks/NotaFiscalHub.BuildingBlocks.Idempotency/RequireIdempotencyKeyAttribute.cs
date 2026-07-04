namespace NotaFiscalHub.BuildingBlocks.Idempotency;

/// <summary>
/// Metadado de rota consumido por <see cref="IdempotencyEndpointFilter"/> (spec B4 §Abordagem passo 3).
/// Rotas de escrita (POST/PUT/PATCH) sob <c>/v1</c> só participam do middleware se declararem um destes
/// dois metadados; GET/DELETE e rotas sem metadado são no-op. <see cref="IdempotencyKeyMode.Obrigatoria"/>
/// (<c>RequireIdempotencyKey()</c>) rejeita requisição sem o header com 400; <see cref="IdempotencyKeyMode.Opcional"/>
/// (<c>AcceptIdempotencyKey()</c>) processa normalmente sem o header, mas aplica o mesmo fluxo de
/// replay/conflito quando o header é enviado.
/// </summary>
public sealed class IdempotencyKeyRouteMetadata(IdempotencyKeyMode modo)
{
    public IdempotencyKeyMode Modo { get; } = modo;
}

public enum IdempotencyKeyMode
{
    Obrigatoria,
    Opcional,
}
