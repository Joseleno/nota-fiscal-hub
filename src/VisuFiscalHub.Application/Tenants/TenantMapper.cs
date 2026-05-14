using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Domain.Entities;

namespace VisuFiscalHub.Application.Tenants;

internal static class TenantMapper
{
    internal static TenantResponse ToResponse(Tenant tenant) =>
        new(
            tenant.Id.Value,
            tenant.ClienteAppId.Value,
            tenant.Cnpj.Valor,
            tenant.RazaoSocial,
            tenant.NomeFantasia,
            tenant.ConfiguracaoFiscal.Crt,
            tenant.ConfiguracaoFiscal.Ambiente,
            tenant.ConfiguracaoFiscal.UfCodigo,
            tenant.ConfiguracaoFiscal.Serie,
            tenant.CertificadoPfxCriptografado is not null,
            tenant.CertificadoVencimento,
            tenant.IsActive,
            tenant.CreatedAt);
}
