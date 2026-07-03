using NetArchTest.Rules;

namespace NotaFiscalHub.ArchitectureTests;

/// <summary>
/// T1 (design §2.3 regra 7 e §2.5) — matriz de referências permitidas entre módulos. Cada teste cobre
/// uma linha (ou uma restrição dura) da matriz documentada em
/// <c>docs/superpowers/specs/tarefas/B6-netarchtest.md</c>. Também contém a fixture de carga de
/// assemblies (guarda contra suíte vazia).
/// </summary>
public class ReferenceRulesTests
{
    [Fact]
    public void Fixture_CarregaPeloMenosUmaAssemblyPorModulo()
    {
        var assemblies = AssemblyLoader.TodasAsAssembliesDaSolution();

        Assert.NotEmpty(assemblies);

        foreach (var modulo in ModuleNames.TodosOsModulosDeNegocio)
        {
            Assert.Contains(assemblies, a => a.GetName().Name!.StartsWith($"{modulo}.", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Emissão pode referenciar EmpresasCertificados.Contracts, MotorNfce.Contracts e
    /// Documentos.Contracts (§2.5) — nunca Domain/Application/Infrastructure de outro módulo, nem
    /// ContasPlanos em nenhuma camada (exceção codificada é só kernel/host, ver
    /// <see cref="NenhumModuloDeNegocio_Referencia_ContasPlanosForaDaAllowlist"/>).
    /// </summary>
    [Fact]
    public void Fronteiras_Emissao_SoReferenciaContratosPermitidos()
    {
        var result = Types.InAssembly(AssemblyLoader.AssemblyApplicationDoModulo(ModuleNames.Emissao))
            .ShouldNot().HaveDependencyOnAny(
                ModuleNames.ContasPlanos,
                $"{ModuleNames.EmpresasCertificados}.Domain", $"{ModuleNames.EmpresasCertificados}.Application", $"{ModuleNames.EmpresasCertificados}.Infrastructure",
                $"{ModuleNames.MotorNfce}.Domain", $"{ModuleNames.MotorNfce}.Application", $"{ModuleNames.MotorNfce}.Infrastructure",
                $"{ModuleNames.Documentos}.Domain", $"{ModuleNames.Documentos}.Application", $"{ModuleNames.Documentos}.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    /// <summary>
    /// Motor NFCe/NFe só pode referenciar EmpresasCertificados.Contracts (§2.5: "CSC/chave não
    /// transitam" — só a operação de assinatura/QR Code é exposta via contrato).
    /// </summary>
    [Fact]
    public void Fronteiras_MotorNfce_SoReferenciaEmpresasCertificadosContracts()
    {
        var result = Types.InAssembly(AssemblyLoader.AssemblyApplicationDoModulo(ModuleNames.MotorNfce))
            .ShouldNot().HaveDependencyOnAny(
                ModuleNames.ContasPlanos,
                $"{ModuleNames.EmpresasCertificados}.Domain", $"{ModuleNames.EmpresasCertificados}.Application", $"{ModuleNames.EmpresasCertificados}.Infrastructure",
                $"{ModuleNames.Emissao}.Domain", $"{ModuleNames.Emissao}.Application", $"{ModuleNames.Emissao}.Infrastructure", $"{ModuleNames.Emissao}.Contracts",
                $"{ModuleNames.Documentos}.Domain", $"{ModuleNames.Documentos}.Application", $"{ModuleNames.Documentos}.Infrastructure", $"{ModuleNames.Documentos}.Contracts")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    /// <summary>Documentos não referencia nenhum outro módulo de negócio (§2.5 — não está listado como origem de nenhuma seta).</summary>
    [Fact]
    public void Fronteiras_Documentos_NaoReferenciaOutrosModulos()
    {
        var result = Types.InAssembly(AssemblyLoader.AssemblyApplicationDoModulo(ModuleNames.Documentos))
            .ShouldNot().HaveDependencyOnAny(
                ModuleNames.ContasPlanos, ModuleNames.EmpresasCertificados, ModuleNames.Emissao, ModuleNames.MotorNfce)
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    /// <summary>EmpresasCertificados não referencia nenhum outro módulo de negócio (§2.5).</summary>
    [Fact]
    public void Fronteiras_EmpresasCertificados_NaoReferenciaOutrosModulos()
    {
        var result = Types.InAssembly(AssemblyLoader.AssemblyApplicationDoModulo(ModuleNames.EmpresasCertificados))
            .ShouldNot().HaveDependencyOnAny(
                ModuleNames.ContasPlanos, ModuleNames.Emissao, ModuleNames.MotorNfce, ModuleNames.Documentos)
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    /// <summary>ContasPlanos não referencia nenhum outro módulo de negócio (§2.5).</summary>
    [Fact]
    public void Fronteiras_ContasPlanos_NaoReferenciaOutrosModulos()
    {
        var result = Types.InAssembly(AssemblyLoader.AssemblyApplicationDoModulo(ModuleNames.ContasPlanos))
            .ShouldNot().HaveDependencyOnAny(
                ModuleNames.EmpresasCertificados, ModuleNames.Emissao, ModuleNames.MotorNfce, ModuleNames.Documentos)
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    /// <summary>
    /// Restrição dura complementar (Tarefa 1: MotorNfce é stateless — nenhum DbContext próprio).
    /// <c>MotorNfce.Infrastructure</c> nunca deve referenciar <c>BuildingBlocks.Persistence</c>: não há
    /// entidade/DbContext no módulo, então nada ali deveria precisar de EF Core/TenantDbContext.
    /// </summary>
    [Fact]
    public void Fronteiras_MotorNfceInfrastructure_NaoReferenciaPersistence()
    {
        var result = Types.InAssembly(AssemblyLoader.AssemblyInfrastructureDoModulo(ModuleNames.MotorNfce))
            .ShouldNot().HaveDependencyOnAny($"{ModuleNames.BuildingBlocks}.Persistence")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    /// <summary>
    /// Regra dura complementar (§2.5, última seta): nenhum módulo de NEGÓCIO referencia ContasPlanos,
    /// em nenhuma camada — só o kernel/host entram na exceção codificada em
    /// <see cref="ArchitectureExceptions"/> (T1: BuildingBlocks → ContasPlanos.Contracts).
    /// </summary>
    [Fact]
    public void NenhumModuloDeNegocio_Referencia_ContasPlanosForaDaAllowlist()
    {
        foreach (var modulo in new[] { ModuleNames.EmpresasCertificados, ModuleNames.Emissao, ModuleNames.MotorNfce, ModuleNames.Documentos })
        {
            foreach (var assembly in AssemblyLoader.AssembliesDoModulo(modulo))
            {
                var result = Types.InAssembly(assembly)
                    .ShouldNot().HaveDependencyOnAny(ModuleNames.ContasPlanos)
                    .GetResult();

                Assert.True(result.IsSuccessful, $"{assembly.GetName().Name} viola: {string.Join(", ", result.FailingTypeNames ?? [])}");
            }
        }
    }
}
