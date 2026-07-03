namespace NotaFiscalHub.Api.Authentication;

/// <summary>
/// Resolve o tenant (conta) da requisição HTTP corrente. A Fase 0 usa <see cref="StubTenantResolver"/>
/// (somente Development/testes); a Fase 1 substitui pela resolução real via API key — sem alterar este
/// contrato (design §2.5, spec B2 passo 5).
/// </summary>
public interface ITenantResolver
{
    Task<(Guid ContaId, IReadOnlyCollection<Guid>? EmpresasPermitidas)?> ResolveAsync(HttpContext context);
}
