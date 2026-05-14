namespace VisuFiscalHub.Domain.ValueObjects;

public sealed record CertificadoDigital(DateTimeOffset VencimentoEm, byte[] PfxBytes);
