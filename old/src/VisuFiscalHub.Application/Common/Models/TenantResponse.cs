using VisuFiscalHub.Domain.Enums;

namespace VisuFiscalHub.Application.Common.Models;

// CSC, PFX, senha never present in this DTO
public sealed record TenantResponse(
    Guid Id,
    Guid ClienteAppId,
    string Cnpj,
    string RazaoSocial,
    string? NomeFantasia,
    RegimeTributario RegimeTributario,
    AmbienteSefaz Ambiente,
    int UfCodigo,
    string Serie,
    bool TemCertificado,
    DateTimeOffset? CertificadoVencimento,
    bool IsActive,
    DateTimeOffset CreatedAt);
