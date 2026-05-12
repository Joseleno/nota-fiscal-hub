namespace VisuFiscalHub.Domain.ValueObjects;

public sealed record CertificadoDigital(DateTime VencimentoEm, byte[] PfxBytes);
