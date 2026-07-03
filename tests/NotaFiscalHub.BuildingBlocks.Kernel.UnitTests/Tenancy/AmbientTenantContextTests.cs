using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;
using Xunit;

namespace NotaFiscalHub.BuildingBlocks.Kernel.UnitTests.Tenancy;

public class AmbientTenantContextTests
{
    [Fact]
    public void ContaId_SemEscopoAtivo_LancaTenantNaoResolvido()
    {
        var context = new AmbientTenantContext();
        Assert.Throws<TenantNaoResolvidoException>(() => _ = context.ContaId);
    }

    [Fact]
    public void BeginTenantScope_Aninhado_RestauraEscopoAnteriorAoDispose()
    {
        var context = new AmbientTenantContext();
        var contaA = Guid.NewGuid();
        var contaB = Guid.NewGuid();
        using (context.BeginTenantScope(contaA))
        {
            using (context.BeginTenantScope(contaB))
            {
                Assert.Equal(contaB, context.ContaId);
            }
            Assert.Equal(contaA, context.ContaId);
        }
        Assert.False(context.HasTenant);
    }

    [Fact]
    public void BeginTenantScope_GuidEmpty_LancaArgumentException()
    {
        var context = new AmbientTenantContext();
        Assert.Throws<ArgumentException>(() => context.BeginTenantScope(Guid.Empty));
    }

    [Fact]
    public void BeginSystemScope_MarcaIsSystemScope()
    {
        var context = new AmbientTenantContext();
        using (context.BeginSystemScope("seed", "migration"))
        {
            Assert.True(context.IsSystemScope);
        }
        Assert.False(context.IsSystemScope);
    }
}
