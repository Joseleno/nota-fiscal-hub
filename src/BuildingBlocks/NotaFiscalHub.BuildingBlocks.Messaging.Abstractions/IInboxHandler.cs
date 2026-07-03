namespace NotaFiscalHub.BuildingBlocks.Messaging.Abstractions;

/// <summary>
/// Consome um evento de integração pela Inbox do módulo. Implementação DEVE ser idempotente e tolerante
/// a reordenação: o dispatcher garante dedupe por <c>(MessageId, Handler)</c>, nunca ordenação.
/// </summary>
public interface IInboxHandler<in T> where T : EventoIntegracao
{
    Task HandleAsync(T evento, MensagemContexto ctx, CancellationToken ct);
}
