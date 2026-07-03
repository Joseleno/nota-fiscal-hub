namespace NotaFiscalHub.BuildingBlocks.Messaging.Abstractions;

/// <summary>
/// Tipo base de todo evento de integração publicado via <see cref="IOutboxPublisher"/>. O payload de um
/// evento derivado só pode conter identificadores/status — NUNCA XML, PDF ou PII (design §2.3.3/§2.3.6).
/// A regra é verificada em build por <c>EventPayloadRules</c> (NetArchTest, Tarefa 3 Step 11).
/// </summary>
public abstract record EventoIntegracao
{
    public Guid MessageId { get; init; } = Guid.CreateVersion7();

    /// <summary>
    /// Tenant dono do evento. Só pode ser <see langword="null"/> para tipos registrados explicitamente
    /// via <c>AddEventoDePlataforma&lt;T&gt;()</c> (eventos de plataforma sem tenant, ex.: manutenção/infra
    /// agendada). Qualquer outro tipo publicado com <c>ContaId = null</c> vai para <c>Poison</c> no dispatch.
    /// </summary>
    public Guid? ContaId { get; init; }

    public DateTimeOffset OcorridoEm { get; init; } = DateTimeOffset.UtcNow;
}
