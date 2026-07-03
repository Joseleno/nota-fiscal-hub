namespace NotaFiscalHub.Api.Authentication;

/// <summary>
/// Resolução interina de tenant, válida SOMENTE em ambiente Development/testes de integração: lê o header
/// <c>X-Nfh-Conta-Id</c> como GUID. Fora de Development (ou header ausente/inválido), retorna <c>null</c>,
/// e o middleware responde 401 — nunca há fallback silencioso para "sem tenant" ou "todos os tenants".
/// A Fase 1 substitui esta implementação pelo pipeline de API key real, mantendo o contrato
/// <see cref="ITenantResolver"/> inalterado.
/// </summary>
public sealed class StubTenantResolver(IHostEnvironment environment) : ITenantResolver
{
    public const string HeaderContaId = "X-Nfh-Conta-Id";

    public Task<(Guid ContaId, IReadOnlyCollection<Guid>? EmpresasPermitidas)?> ResolveAsync(HttpContext context)
    {
        if (!environment.IsDevelopment())
        {
            return Task.FromResult<(Guid, IReadOnlyCollection<Guid>?)?>(null);
        }

        if (!context.Request.Headers.TryGetValue(HeaderContaId, out var valores)
            || !Guid.TryParse(valores.ToString(), out var contaId)
            || contaId == Guid.Empty)
        {
            return Task.FromResult<(Guid, IReadOnlyCollection<Guid>?)?>(null);
        }

        return Task.FromResult<(Guid, IReadOnlyCollection<Guid>?)?>((contaId, null));
    }
}
