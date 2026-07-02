namespace VisuFiscalHub.Application.Common.Models;

public sealed record CertificadoStatusResponse(
    DateTimeOffset? VencimentoEm,
    int? DiasRestantes,
    bool TemCertificado,
    bool Expirado);
