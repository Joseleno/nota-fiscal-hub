using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;
using NotaFiscalHub.Worker;
using Xunit;

namespace NotaFiscalHub.IntegrationTests.Tenancy;

/// <summary>
/// Prova que <c>TenantJobExecutor.Execute</c> abre o escopo de tenant do job, e que um job que resolve
/// diretamente um serviço tenant-scoped SEM passar pelo executor falha — "job sem escopo é erro, não
/// 'processa tudo'" (design §2.2, spec B2 passo 6).
/// </summary>
public class TenantJobExecutorTests
{
    [Fact]
    public async Task Execute_AbreEscopoDoContaIdInformado()
    {
        var tenantContext = new AmbientTenantContext();
        var executor = new TenantJobExecutor(tenantContext);
        var contaId = Guid.NewGuid();
        Guid? contaIdVistaPeloJob = null;

        await executor.Execute(contaId, () =>
        {
            contaIdVistaPeloJob = tenantContext.ContaId;
            return Task.CompletedTask;
        });

        Assert.Equal(contaId, contaIdVistaPeloJob);
        Assert.False(tenantContext.HasTenant);
    }

    [Fact]
    public void Execute_SemEscopoNoJob_JobQueChamaContaId_Lanca()
    {
        var tenantContext = new AmbientTenantContext();

        // Job que resolve o tenant ambiente SEM passar por TenantJobExecutor.Execute (chamado direto,
        // simulando um handler tenant-scoped invocado fora do executor) — deve lançar, provando que o
        // isolamento não depende de disciplina do chamador: é fail-closed por padrão, não por convenção.
        void JobSemEscopo() => _ = tenantContext.ContaId;

        Assert.Throws<TenantNaoResolvidoException>(JobSemEscopo);
    }
}
