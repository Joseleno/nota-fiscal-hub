using NetArchTest.Rules;

namespace NotaFiscalHub.ArchitectureTests;

/// <summary>
/// T3 (design §2.3 regra 3, combinado com T2) e T4 (Clean Architecture: Domain é a camada mais
/// interna) — entidades de domínio nunca cruzam fronteira de módulo, e a camada Domain de cada módulo
/// não referencia sua própria Infrastructure/Application nem frameworks (EF Core, ASP.NET Core).
/// </summary>
public class DomainIsolationTests
{
    /// <summary>
    /// T3: para cada módulo, nenhum assembly de OUTRO módulo referencia a sua Domain. Comunicação
    /// síncrona só carrega DTOs de Contracts (T2) — este teste fecha o outro lado: mesmo que um
    /// contrato seja puro, nada impede um consumidor de referenciar a Domain diretamente por engano
    /// (ex.: <c>Documentos.Application</c> usando um tipo de <c>Emissao.Domain</c>); T3 barra isso.
    /// </summary>
    [Fact]
    public void EntidadeDeDominio_NaoEUsadaForaDoProprioModulo()
    {
        foreach (var modulo in ModuleNames.TodosOsModulosDeNegocio)
        {
            var result = Types.InAssemblies(AssemblyLoader.TodosOsAssembliesDeModulosExceto(modulo))
                .ShouldNot().HaveDependencyOnAny($"{modulo}.Domain")
                .GetResult();

            Assert.True(result.IsSuccessful, $"{modulo}.Domain: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    /// <summary>
    /// T4: a Domain de cada módulo de negócio não referencia a própria Infrastructure/Application
    /// (a seta de dependência corre Infrastructure → Application → Domain, nunca o inverso) nem
    /// EF Core/ASP.NET Core — Domain é POCO puro (Clean Architecture).
    /// </summary>
    [Fact]
    public void Domain_NaoReferenciaInfrastructureNemFrameworks()
    {
        foreach (var modulo in ModuleNames.TodosOsModulosDeNegocio)
        {
            var result = Types.InAssembly(AssemblyLoader.AssemblyDomainDoModulo(modulo))
                .ShouldNot().HaveDependencyOnAny(
                    $"{modulo}.Infrastructure", $"{modulo}.Application",
                    "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore")
                .GetResult();

            Assert.True(result.IsSuccessful, $"{modulo}: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    /// <summary>Também coberto individualmente por módulo — mantém o nome histórico da Tarefa 1 como regressão dedicada da Emissão.</summary>
    [Fact]
    public void Domain_NaoReferenciaInfrastructure()
    {
        var result = Types.InAssembly(AssemblyLoader.AssemblyDomainDoModulo(ModuleNames.Emissao))
            .That().ResideInNamespace($"{ModuleNames.Emissao}.Domain")
            .ShouldNot().HaveDependencyOn($"{ModuleNames.Emissao}.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }
}
