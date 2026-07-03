using NetArchTest.Rules;
using Xunit;

namespace NotaFiscalHub.ArchitectureTests;

public class ReferenceRulesTests
{
    [Fact]
    public void Domain_NaoReferenciaInfrastructure()
    {
        var result = Types.InAssembly(typeof(Modules.Emissao.Domain.AssemblyMarker).Assembly)
            .That().ResideInNamespace("NotaFiscalHub.Modules.Emissao.Domain")
            .ShouldNot().HaveDependencyOn("NotaFiscalHub.Modules.Emissao.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }
}
