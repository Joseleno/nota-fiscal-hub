using NetArchTest.Rules;

namespace NotaFiscalHub.ArchitectureTests;

/// <summary>
/// T2 (design §2.3 regra 3) — contratos só carregam DTOs. Comunicação síncrona entre módulos passa só
/// por <c>*.Contracts</c>, que não pode depender de EF Core (vazaria detalhe de persistência) nem de
/// Domain/Application/Infrastructure de nenhum módulo (vazaria entidade/regra de negócio).
/// </summary>
public class ContractPurityTests
{
    [Fact]
    public void Contratos_NaoReferenciaEntityFrameworkCore()
    {
        foreach (var contracts in AssemblyLoader.TodosOsAssembliesDeContracts())
        {
            var result = Types.InAssembly(contracts)
                .ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore")
                .GetResult();

            Assert.True(result.IsSuccessful, $"{contracts.GetName().Name}: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    /// <summary>
    /// Nenhum assembly <c>*.Contracts</c> depende de Domain/Application/Infrastructure — nem do
    /// próprio módulo, nem de outro. Um contrato que referencia sua própria camada de domínio é o
    /// sintoma clássico de "expor a entidade em vez do DTO" (violação-isca do Step 4).
    /// </summary>
    [Fact]
    public void Contratos_NaoExpoeEntidadesDeDominio()
    {
        foreach (var modulo in ModuleNames.TodosOsModulosDeNegocio)
        {
            var contracts = AssemblyLoader.AssemblyContractsDoModulo(modulo);

            var proibidos = ModuleNames.TodosOsModulosDeNegocio
                .SelectMany(m => new[] { $"{m}.Domain", $"{m}.Application", $"{m}.Infrastructure" })
                .ToArray();

            var result = Types.InAssembly(contracts)
                .ShouldNot().HaveDependencyOnAny(proibidos)
                .GetResult();

            Assert.True(result.IsSuccessful, $"{contracts.GetName().Name}: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }
}
