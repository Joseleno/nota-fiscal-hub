namespace NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;

/// <summary>
/// Lançada quando o <c>TenantWriteInterceptor</c> detecta, fora de escopo de sistema,
/// uma tentativa de gravar uma entidade tenant-scoped cujo <c>ContaId</c> não corresponde
/// ao tenant ambiente ativo. Protege contra vazamento de dados entre contas em escritas.
/// </summary>
public sealed class TenantMismatchException(Guid contaIdEsperada, Guid contaIdEntidade)
    : InvalidOperationException(
        $"Entidade com ContaId '{contaIdEntidade}' não pode ser gravada no escopo do tenant '{contaIdEsperada}'.")
{
    public Guid ContaIdEsperada { get; } = contaIdEsperada;
    public Guid ContaIdEntidade { get; } = contaIdEntidade;
}
