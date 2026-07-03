using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;

namespace NotaFiscalHub.BuildingBlocks.Persistence;

/// <summary>
/// Por padrão o EF Core cacheia o <c>IModel</c> compilado por TIPO de <c>DbContext</c> (não por
/// instância) — <see cref="ModelCacheKeyFactory"/>. Como o query filter de tenant (<see cref="TenantDbContext"/>)
/// fecha sobre a instância de <see cref="ITenantContext"/> recebida no construtor, um cache por tipo faria
/// TODAS as instâncias de um mesmo <c>&lt;Modulo&gt;DbContext</c> reusarem o filtro compilado a partir da
/// PRIMEIRA instância de <see cref="ITenantContext"/> vista — silenciosamente errado (ou, em testes que criam
/// um <see cref="AmbientTenantContext"/> novo por caso, uma falha intermitente por closure obsoleta).
///
/// Este factory inclui a instância de <see cref="ITenantContext"/> na chave de cache, de modo que cada
/// combinação (tipo de DbContext, instância de tenant context) tem seu próprio modelo compilado. Em produção,
/// onde <see cref="ITenantContext"/> é registrado como singleton (o mesmo objeto ambiente para toda a
/// aplicação, variando por <c>AsyncLocal</c> internamente), isso equivale, na prática, a um único modelo
/// cacheado por tipo — o comportamento observável de performance não muda; o que muda é a correção sob
/// múltiplas instâncias de tenant context (como em testes).
/// </summary>
public sealed class TenantModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
    {
        var tenantContext = context is TenantDbContext td ? td.TenantContextIdentity : null;
        return (context.GetType(), tenantContext, designTime);
    }
}
