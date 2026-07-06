namespace NotaFiscalHub.BuildingBlocks.Observability;

/// <summary>
/// CorrelationId ambiente do fluxo de execução corrente (spec B7 passo 3). Nunca nulo/vazio depois da
/// borda: na API, <see cref="CorrelationIdMiddleware"/> garante um valor (header válido ou GUID novo)
/// antes de qualquer código de aplicação rodar; no Worker, o dispatcher da Outbox (Tarefa 3) restaura o
/// valor gravado no envelope antes de invocar o <c>IInboxHandler</c> — nunca herdado do ciclo de
/// processamento anterior (spec B7, "Riscos": redelivery não pode vazar CorrelationId de outra mensagem).
/// </summary>
public interface ICorrelationContext
{
    /// <summary>
    /// CorrelationId do fluxo corrente. Lança se lido antes de <see cref="CorrelationContext.Definir"/>
    /// ser chamado no fluxo atual — nunca retorna vazio/nulo silenciosamente (ver <see cref="CorrelationContext"/>).
    /// </summary>
    string CorrelationId { get; }
}
