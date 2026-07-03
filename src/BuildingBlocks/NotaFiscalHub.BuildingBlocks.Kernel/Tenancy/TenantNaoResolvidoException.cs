namespace NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;

/// <summary>
/// Lançada quando código tenta acessar o tenant ambiente (<see cref="ITenantContext.ContaId"/>)
/// sem que exista um escopo de tenant ativo (nem de negócio, nem de sistema).
/// Fail-closed: a ausência de escopo é sempre um erro, nunca um "acesso irrestrito" implícito.
/// </summary>
public sealed class TenantNaoResolvidoException()
    : InvalidOperationException("Nenhum escopo de tenant ativo. Use BeginTenantScope ou BeginSystemScope antes de acessar o contexto de tenant.");
