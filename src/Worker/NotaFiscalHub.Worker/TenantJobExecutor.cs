using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;

namespace NotaFiscalHub.Worker;

/// <summary>
/// Envolve todo job do Worker que precisa operar sob um tenant específico: abre o escopo, executa o job
/// e garante o dispose (mesmo em caso de exceção, via <c>using</c>) — o job só enxerga o tenant informado
/// e o escopo nunca vaza para a próxima execução no mesmo thread do pool. Um job que resolve um serviço
/// tenant-scoped SEM passar por este executor falha com <see cref="TenantNaoResolvidoException"/>: é erro,
/// não "processa tudo" (design §2.2, spec B2 passo 6) — o isolamento é garantia do kernel, não disciplina
/// do autor do job.
/// </summary>
public sealed class TenantJobExecutor(ITenantScopeFactory scopeFactory)
{
    public async Task Execute(Guid contaId, Func<Task> job)
    {
        using var scope = scopeFactory.BeginTenantScope(contaId);
        await job();
    }
}
