// NOTE: record struct inheritance limitation
// readonly record struct não pode herdar de record (class).
// O contrato de StronglyTypedId<T> é replicado manualmente aqui.
namespace VisuFiscalHub.Domain.Identifiers;

public readonly record struct DocumentoFiscalId(Guid Value)
{
    public static DocumentoFiscalId New() => new(Guid.NewGuid());
    public static DocumentoFiscalId From(Guid value) => new(value);

    public static implicit operator Guid(DocumentoFiscalId id) => id.Value;
    public static explicit operator DocumentoFiscalId(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
