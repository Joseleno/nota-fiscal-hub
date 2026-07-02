using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Application.Common.Interfaces;

public interface ISequenceManager
{
    Task<Result> EnsureNumeracaoSequenceAsync(TenantId tenantId, string serie, CancellationToken ct = default);
    Task<Result<long>> GetNextNumeroAsync(TenantId tenantId, string serie, CancellationToken ct = default);
}
