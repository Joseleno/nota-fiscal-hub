using System.Reflection;

namespace NotaFiscalHub.ArchitectureTests;

/// <summary>
/// Carrega e resolve os assemblies da solution por convenção de nome
/// (<c>NotaFiscalHub.*</c>). Cada projeto referenciado por
/// <c>NotaFiscalHub.ArchitectureTests.csproj</c> só entra em
/// <see cref="AppDomain.CurrentDomain"/> quando um tipo seu é tocado — por isso todo projeto tem um
/// <c>AssemblyMarker</c> no namespace raiz: força o carregamento determinístico do assembly antes da
/// suíte rodar, em vez de depender de carregamento tardio/lazy do runtime.
/// </summary>
public static class AssemblyLoader
{
    /// <summary>
    /// Tipos-marcadores usados apenas para forçar o carregamento eager do assembly correspondente.
    /// Um por projeto referenciado por este projeto de testes.
    /// </summary>
    private static readonly Type[] Marcadores =
    [
        typeof(Modules.ContasPlanos.Domain.AssemblyMarker),
        typeof(Modules.ContasPlanos.Application.AssemblyMarker),
        typeof(Modules.ContasPlanos.Contracts.AssemblyMarker),
        typeof(Modules.ContasPlanos.Infrastructure.AssemblyMarker),
        typeof(Modules.EmpresasCertificados.Domain.AssemblyMarker),
        typeof(Modules.EmpresasCertificados.Application.AssemblyMarker),
        typeof(Modules.EmpresasCertificados.Contracts.AssemblyMarker),
        typeof(Modules.EmpresasCertificados.Infrastructure.AssemblyMarker),
        typeof(Modules.Emissao.Domain.AssemblyMarker),
        typeof(Modules.Emissao.Application.AssemblyMarker),
        typeof(Modules.Emissao.Contracts.AssemblyMarker),
        typeof(Modules.Emissao.Infrastructure.AssemblyMarker),
        typeof(Modules.MotorNfce.Domain.AssemblyMarker),
        typeof(Modules.MotorNfce.Application.AssemblyMarker),
        typeof(Modules.MotorNfce.Contracts.AssemblyMarker),
        typeof(Modules.MotorNfce.Infrastructure.AssemblyMarker),
        typeof(Modules.Documentos.Domain.AssemblyMarker),
        typeof(Modules.Documentos.Application.AssemblyMarker),
        typeof(Modules.Documentos.Contracts.AssemblyMarker),
        typeof(Modules.Documentos.Infrastructure.AssemblyMarker),
        typeof(BuildingBlocks.Kernel.AssemblyMarker),
        typeof(BuildingBlocks.Persistence.AssemblyMarker),
    ];

    /// <summary>
    /// Todos os assemblies <c>NotaFiscalHub.*</c> atualmente carregados no domínio, após forçar o
    /// carregamento eager de todo marcador conhecido. Falha de forma visível (suíte vazia) é
    /// evitada pelo teste <c>Fixture_CarregaPeloMenosUmaAssemblyPorModulo</c>, não por esta fixture.
    /// </summary>
    public static IReadOnlyCollection<Assembly> TodasAsAssembliesDaSolution()
    {
        _ = Marcadores;

        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name!.StartsWith("NotaFiscalHub", StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>Assembly cujo nome simples é exatamente <paramref name="nomeAssembly"/>.</summary>
    public static Assembly AssemblyPorNome(string nomeAssembly) =>
        TodasAsAssembliesDaSolution().SingleOrDefault(a => a.GetName().Name == nomeAssembly)
        ?? throw new InvalidOperationException(
            $"Assembly \"{nomeAssembly}\" não está carregado. Verifique se há um ProjectReference (direto ou " +
            $"transitivo) e um AssemblyMarker referenciado em {nameof(AssemblyLoader)}.{nameof(Marcadores)}.");

    /// <summary>Assembly <c>{modulo}.Application</c>.</summary>
    public static Assembly AssemblyApplicationDoModulo(string modulo) => AssemblyPorNome($"{modulo}.Application");

    /// <summary>Assembly <c>{modulo}.Domain</c>.</summary>
    public static Assembly AssemblyDomainDoModulo(string modulo) => AssemblyPorNome($"{modulo}.Domain");

    /// <summary>Assembly <c>{modulo}.Infrastructure</c>.</summary>
    public static Assembly AssemblyInfrastructureDoModulo(string modulo) => AssemblyPorNome($"{modulo}.Infrastructure");

    /// <summary>Assembly <c>{modulo}.Contracts</c>.</summary>
    public static Assembly AssemblyContractsDoModulo(string modulo) => AssemblyPorNome($"{modulo}.Contracts");

    /// <summary>
    /// Todos os assemblies das quatro camadas (Domain/Application/Infrastructure/Contracts) de um módulo
    /// que estiverem carregados — MotorNfce ainda não tem Infrastructure com conteúdo próprio de negócio,
    /// então a busca é tolerante à ausência de uma camada.
    /// </summary>
    public static IReadOnlyCollection<Assembly> AssembliesDoModulo(string modulo) =>
        TodasAsAssembliesDaSolution()
            .Where(a => a.GetName().Name!.StartsWith($"{modulo}.", StringComparison.Ordinal))
            .ToList();

    /// <summary>Assemblies de todos os módulos de negócio (exclui BuildingBlocks e hosts).</summary>
    public static IReadOnlyCollection<Assembly> TodosOsAssembliesDeModulos() =>
        ModuleNames.TodosOsModulosDeNegocio.SelectMany(AssembliesDoModulo).ToList();

    /// <summary>Assemblies <c>*.Contracts</c> de todos os módulos de negócio.</summary>
    public static IReadOnlyCollection<Assembly> TodosOsAssembliesDeContracts() =>
        ModuleNames.TodosOsModulosDeNegocio.Select(AssemblyContractsDoModulo).ToList();

    /// <summary>Assemblies do kernel (<c>NotaFiscalHub.BuildingBlocks.*</c>).</summary>
    public static IReadOnlyCollection<Assembly> TodosOsAssembliesDeBuildingBlocks() =>
        TodasAsAssembliesDaSolution()
            .Where(a => a.GetName().Name!.StartsWith($"{ModuleNames.BuildingBlocks}.", StringComparison.Ordinal))
            .ToList();

    /// <summary>Todos os assemblies de módulo que NÃO pertencem ao módulo informado (para regras T3/T5-estilo).</summary>
    public static IReadOnlyCollection<Assembly> TodosOsAssembliesDeModulosExceto(string modulo) =>
        ModuleNames.TodosOsModulosDeNegocio
            .Where(m => m != modulo)
            .SelectMany(AssembliesDoModulo)
            .ToList();
}
