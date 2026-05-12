// NOTE: record struct inheritance limitation
// readonly record struct não pode herdar de record (class).
// O contrato de StronglyTypedId<T> é replicado manualmente aqui.
namespace VisuFiscalHub.Domain.Identifiers;

public readonly record struct ClienteAppId(Guid Value)
{
    public static ClienteAppId New() => new(Guid.NewGuid());
    public static ClienteAppId From(Guid value) => new(value);

    public static implicit operator Guid(ClienteAppId id) => id.Value;
    public static explicit operator ClienteAppId(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
