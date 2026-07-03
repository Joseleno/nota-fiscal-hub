using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;

/// <summary>
/// Implementação ambiente de <see cref="ITenantContext"/>/<see cref="ITenantScopeFactory"/> baseada em
/// <see cref="AsyncLocal{T}"/>. O estado é uma pilha imutável (lista encadeada) de <see cref="TenantScopeState"/>:
/// cada <see cref="BeginTenantScope"/>/<see cref="BeginSystemScope"/> empilha um novo estado e o <c>Dispose</c>
/// do escopo retorna o <see cref="AsyncLocal{T}"/> para o estado anterior — nunca "limpa tudo", o que preserva
/// aninhamento correto mesmo com escopos abertos fora de ordem por código descuidado.
///
/// <see cref="AsyncLocal{T}"/> flui automaticamente por <c>async</c>/<c>await</c> e é isolado por fluxo de
/// execução lógico: duas requisições concorrentes (ou dois branches de <c>Task.WhenAll</c>) nunca compartilham
/// o mesmo valor, mesmo que sejam servidas pela mesma thread do pool em momentos diferentes.
/// </summary>
public sealed class AmbientTenantContext(ILogger<AmbientTenantContext>? logger = null) : ITenantContext, ITenantScopeFactory
{
    private readonly ILogger<AmbientTenantContext> _logger = logger ?? NullLogger<AmbientTenantContext>.Instance;
    private readonly AsyncLocal<TenantScopeState?> _estadoAtual = new();

    public Guid ContaId => _estadoAtual.Value is { ContaId: var contaId } ? contaId : throw new TenantNaoResolvidoException();

    public bool HasTenant => _estadoAtual.Value is not null;

    public bool IsSystemScope => _estadoAtual.Value?.IsSystemScope ?? false;

    public IReadOnlyCollection<Guid>? EmpresasPermitidas => _estadoAtual.Value?.EmpresasPermitidas;

    public ITenantScope BeginTenantScope(Guid contaId)
    {
        if (contaId == Guid.Empty)
            throw new ArgumentException("ContaId não pode ser Guid.Empty.", nameof(contaId));

        return PushScope(new TenantScopeState(contaId, IsSystemScope: false, EmpresasPermitidas: null));
    }

    public ITenantScope BeginSystemScope(string motivo, string origem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(motivo);
        ArgumentException.ThrowIfNullOrWhiteSpace(origem);

        var escopo = PushScope(new TenantScopeState(Guid.Empty, IsSystemScope: true, EmpresasPermitidas: null));

        _logger.LogInformation(
            "TenantScopeBypassed: escopo de sistema aberto. Motivo={Motivo} Origem={Origem}",
            motivo, origem);

        return escopo;
    }

    private ITenantScope PushScope(TenantScopeState novoEstado)
    {
        var estadoAnterior = _estadoAtual.Value;
        _estadoAtual.Value = novoEstado;
        return new TenantScopeHandle(() => _estadoAtual.Value = estadoAnterior);
    }

    private sealed record TenantScopeState(Guid ContaId, bool IsSystemScope, IReadOnlyCollection<Guid>? EmpresasPermitidas);

    private sealed class TenantScopeHandle(Action aoDispor) : ITenantScope
    {
        private bool _disposto;

        public void Dispose()
        {
            if (_disposto) return;
            _disposto = true;
            aoDispor();
        }
    }
}
