// NOTE: record struct inheritance limitation
// readonly record struct não pode herdar de record (class).
// O contrato de StronglyTypedId<T> é replicado manualmente aqui.
namespace VisuFiscalHub.Domain.Identifiers;

public readonly record struct TenantId(Guid Value)
{
    public static TenantId New() => new(Guid.CreateVersion7());
    public static TenantId From(Guid value) => new(value);

    public static implicit operator Guid(TenantId id) => id.Value;
    public static explicit operator TenantId(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
