namespace NotaFiscalHub.BuildingBlocks.Messaging.Abstractions;

/// <summary>Contexto de entrega passado a todo <see cref="IInboxHandler{T}"/> na execução de um handler.</summary>
public sealed record MensagemContexto(Guid MessageId, string CorrelationId, int Tentativa);
