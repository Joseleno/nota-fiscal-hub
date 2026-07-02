namespace VisuFiscalHub.Domain.Common;

// NOTE: record struct inheritance limitation
// Em C#, record struct não suporta herança de outro record struct (tipos de valor não suportam herança).
// 'abstract record struct' compila mas os concretos não conseguem herdar dela como struct.
// Solução: StronglyTypedId é definido como abstract record (class) para que sirvam de documentação
// do contrato. Os identificadores concretos são readonly record struct independentes que replicam
// o contrato manualmente — isso garante alocação na stack e comparação por valor.
public abstract record StronglyTypedId<T>(T Value)
    where T : notnull;
