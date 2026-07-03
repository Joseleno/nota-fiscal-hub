namespace NotaFiscalHub.ArchitectureTests;

/// <summary>
/// Nomes de módulo centralizados — toda regra de fronteira referencia estas constantes em vez de
/// strings soltas, para que uma renomeação de módulo quebre a compilação da suíte em vez de deixar
/// uma regra silenciosamente incapaz de encontrar seu assembly (design §2.3, "Riscos").
/// </summary>
public static class ModuleNames
{
    public const string ContasPlanos = "NotaFiscalHub.Modules.ContasPlanos";
    public const string EmpresasCertificados = "NotaFiscalHub.Modules.EmpresasCertificados";
    public const string Emissao = "NotaFiscalHub.Modules.Emissao";
    public const string MotorNfce = "NotaFiscalHub.Modules.MotorNfce";
    public const string Documentos = "NotaFiscalHub.Modules.Documentos";
    public const string BuildingBlocks = "NotaFiscalHub.BuildingBlocks";

    /// <summary>
    /// Todos os módulos de negócio (exclui o kernel/BuildingBlocks, que não é módulo de negócio).
    /// </summary>
    public static readonly IReadOnlyList<string> TodosOsModulosDeNegocio =
    [
        ContasPlanos, EmpresasCertificados, Emissao, MotorNfce, Documentos,
    ];
}
