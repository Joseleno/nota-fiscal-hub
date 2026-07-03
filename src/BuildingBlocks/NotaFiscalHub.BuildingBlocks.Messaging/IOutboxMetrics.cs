using System.Diagnostics.Metrics;

namespace NotaFiscalHub.BuildingBlocks.Messaging;

/// <summary>
/// Métricas do dispatcher (spec B3 critério 4/6: <c>outbox_mensagens_sem_handler_total</c>,
/// <c>outbox_mensagens_poison_total</c>). Abstração pequena para permitir teste sem depender do
/// pipeline completo de observabilidade (Tarefa B7, ainda não integrada nesta tarefa).
/// </summary>
public interface IOutboxMetrics
{
    void IncrementarSemHandler();
    void IncrementarPoison();
}

/// <summary>Implementação real via <see cref="System.Diagnostics.Metrics.Meter"/> (OpenTelemetry-compatible).</summary>
public sealed class OutboxMetrics : IOutboxMetrics
{
    private static readonly Meter Meter = new("NotaFiscalHub.BuildingBlocks.Messaging");
    private static readonly Counter<long> SemHandlerCounter = Meter.CreateCounter<long>("outbox_mensagens_sem_handler_total");
    private static readonly Counter<long> PoisonCounter = Meter.CreateCounter<long>("outbox_mensagens_poison_total");

    public void IncrementarSemHandler() => SemHandlerCounter.Add(1);
    public void IncrementarPoison() => PoisonCounter.Add(1);
}

/// <summary>No-op — usado quando o chamador não registra métricas explicitamente (default seguro em testes).</summary>
public sealed class NullOutboxMetrics : IOutboxMetrics
{
    public static readonly NullOutboxMetrics Instance = new();
    public void IncrementarSemHandler() { }
    public void IncrementarPoison() { }
}
