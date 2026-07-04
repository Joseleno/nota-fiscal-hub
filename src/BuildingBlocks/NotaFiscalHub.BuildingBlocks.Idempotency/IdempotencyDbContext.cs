using Microsoft.EntityFrameworkCore;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;
using NotaFiscalHub.BuildingBlocks.Persistence;

namespace NotaFiscalHub.BuildingBlocks.Idempotency;

/// <summary>
/// DbContext dedicado à tabela <c>kernel.idempotency_registro</c>. Diferente dos DbContexts de módulo
/// (um por módulo de negócio, schema próprio do módulo), este é único e compartilhado por toda a
/// aplicação — o schema <c>kernel</c> não pertence a nenhum módulo de negócio (spec B4 §Abordagem passo 2,
/// mesma exceção do schema <c>auditoria</c> da Tarefa 5).
///
/// Herda <see cref="TenantDbContext"/> pelo mesmo motivo de qualquer DbContext com entidade
/// <see cref="ITenantScopedEntity"/>, mas <see cref="IdempotencyRecordEntity"/> está na allowlist de
/// entidades globais (ver seu comentário de classe) — o filtro "Tenant" fica registrado (satisfaz o
/// teste de arquitetura que verifica a presença do filtro em toda entidade tenant-scoped mapeada), porém
/// o job de expiração precisa contornar esse filtro para a varredura cross-tenant; como o contorno só é
/// permitido dentro do kernel (<c>IgnoreQueryFilters</c> restrito a <c>src/BuildingBlocks</c>), e este
/// próprio DbContext já vive lá, o uso é local e não vaza para módulos de negócio.
/// </summary>
public sealed class IdempotencyDbContext(DbContextOptions<IdempotencyDbContext> options, ITenantContext tenantContext)
    : TenantDbContext(options, tenantContext)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AplicarIdempotency();
    }
}
