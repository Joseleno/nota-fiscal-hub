using NetArchTest.Rules;

namespace NotaFiscalHub.ArchitectureTests;

/// <summary>
/// T7 (design §2.3 regra 7) — a estrutura física de projetos espelha a estrutura lógica de módulos.
/// Nenhum módulo referencia os hosts (a dependência é sempre host → módulo, nunca o inverso) e o
/// kernel (BuildingBlocks) não referencia módulo de negócio nenhum fora da exceção codificada em
/// <see cref="ArchitectureExceptions"/> (T1: BuildingBlocks/host → ContasPlanos.Contracts).
/// </summary>
public class PhysicalStructureTests
{
    [Fact]
    public void Modulos_NaoReferenciamHosts()
    {
        var result = Types.InAssemblies(AssemblyLoader.TodosOsAssembliesDeModulos())
            .ShouldNot().HaveDependencyOnAny("NotaFiscalHub.Api", "NotaFiscalHub.Worker")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    /// <summary>
    /// A única exceção codificada é BuildingBlocks → ContasPlanos.Contracts (Step 9). Nenhum outro
    /// módulo de negócio pode ser referenciado pelo kernel.
    /// </summary>
    [Fact]
    public void BuildingBlocks_NaoReferenciaModulosForaDaExcecaoCodificada()
    {
        var result = Types.InAssemblies(AssemblyLoader.TodosOsAssembliesDeBuildingBlocks())
            .ShouldNot().HaveDependencyOnAny(
                $"{ModuleNames.EmpresasCertificados}", $"{ModuleNames.Emissao}", $"{ModuleNames.MotorNfce}", $"{ModuleNames.Documentos}")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }
}
