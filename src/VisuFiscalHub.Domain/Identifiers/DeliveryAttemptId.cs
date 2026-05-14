// NOTE: record struct inheritance limitation
// readonly record struct não pode herdar de record (class).
// O contrato de StronglyTypedId<T> é replicado manualmente aqui.
namespace VisuFiscalHub.Domain.Identifiers;

public readonly record struct DeliveryAttemptId(Guid Value)
{
    public static DeliveryAttemptId New() => new(Guid.CreateVersion7());
    public static DeliveryAttemptId From(Guid value) => new(value);

    public static implicit operator Guid(DeliveryAttemptId id) => id.Value;
    public static explicit operator DeliveryAttemptId(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
