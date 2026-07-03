namespace NotaFiscalHub.BuildingBlocks.Messaging;

/// <summary>
/// Linha da tabela <c>inbox</c> do schema do módulo consumidor (spec B3 passo 2). A chave composta
/// <c>(MessageId, Handler)</c> é o mecanismo de dedupe primário — dedupe é POR HANDLER (permite N
/// handlers inscritos no mesmo evento, cada um com sua própria janela de idempotência) e é imposto pela
/// constraint de unicidade da PK via <c>INSERT ... ON CONFLICT DO NOTHING</c>, nunca "check-then-insert"
/// (que teria condição de corrida sob concorrência).
/// </summary>
public sealed class InboxMessage
{
    public Guid MessageId { get; set; }

    /// <summary>Nome completo do tipo de <see cref="Messaging.Abstractions.IInboxHandler{T}"/> que processou.</summary>
    public string Handler { get; set; } = string.Empty;

    public string TipoEvento { get; set; } = string.Empty;

    public DateTimeOffset ProcessadaEm { get; set; }
}
