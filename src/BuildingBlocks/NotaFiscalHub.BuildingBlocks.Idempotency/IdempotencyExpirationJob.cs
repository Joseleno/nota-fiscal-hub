using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;

namespace NotaFiscalHub.BuildingBlocks.Idempotency;

/// <summary>
/// Job de expiração de <c>kernel.idempotency_registro</c> (spec B4 §Abordagem passo 4). Roda em
/// VARREDURA POR TENANT — Global Constraint do plano-mestre ("BeginTenantScope em todo job do Worker"):
/// primeiro enumera as <c>conta_id</c> distintas com registros vencidos (consulta de sistema, cross-tenant,
/// por isso <c>IgnoreQueryFilters(["Tenant"])</c> dentro de um <c>BeginSystemScope</c> — uso restrito ao
/// kernel, ver <c>TenantScopedEntityTests.IgnoreQueryFilters_ProibidoForaDoKernel</c>), depois, PARA CADA
/// CONTA, abre <see cref="ITenantScopeFactory.BeginTenantScope"/> e apaga em lote só os registros vencidos
/// DESSA conta. Remove por <c>ExpiraEm</c> vencido em qualquer estado (spec B4 §Abordagem passo 5, último
/// parágrafo) — não é o mecanismo de liberação do órfão (isso é o takeover em <see cref="IdempotencyStore"/>).
/// </summary>
public sealed class IdempotencyExpirationJob(
    IServiceScopeFactory scopeFactory, TimeProvider relogio, ILogger<IdempotencyExpirationJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan IntervaloEntreCiclos = TimeSpan.FromHours(1);
    private const int TamanhoDoLote = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ExecutarUmCicloAsync(stoppingToken);

            try
            {
                await Task.Delay(IntervaloEntreCiclos, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Executa um ciclo de expiração. Público para permitir disparo determinístico em testes.</summary>
    public async Task ExecutarUmCicloAsync(CancellationToken ct = default)
    {
        var agora = relogio.GetUtcNow();

        var contasComVencidos = await ContasComRegistrosVencidosAsync(agora, ct);

        var totalRemovido = 0;
        foreach (var contaId in contasComVencidos)
        {
            totalRemovido += await RemoverVencidosDaContaAsync(contaId, agora, ct);
        }

        logger.LogInformation(
            "IdempotencyExpirationCiclo: {TotalContas} conta(s) com registros vencidos, {TotalRemovido} linha(s) removida(s).",
            contasComVencidos.Count, totalRemovido);
    }

    /// <summary>
    /// Varredura de sistema cross-tenant: precisa enxergar registros vencidos de QUALQUER conta antes de
    /// decidir para quais contas abrir escopo — por isso ignora o filtro "Tenant" (kernel-only) dentro de
    /// um <see cref="ITenantScopeFactory.BeginSystemScope"/> explícito e auditável, em vez de já operar
    /// dentro de um <c>BeginTenantScope</c> (que restringiria a própria consulta a uma conta).
    /// </summary>
    private async Task<List<Guid>> ContasComRegistrosVencidosAsync(DateTimeOffset agora, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdempotencyDbContext>();
        var tenantScopeFactory = scope.ServiceProvider.GetRequiredService<ITenantScopeFactory>();

        using var _ = tenantScopeFactory.BeginSystemScope("expiracao-idempotency", nameof(IdempotencyExpirationJob));

        return await db.Set<IdempotencyRecordEntity>()
            .IgnoreQueryFilters(["Tenant"])
            .Where(r => r.ExpiraEm < agora)
            .Select(r => r.ContaId)
            .Distinct()
            .ToListAsync(ct);
    }

    private async Task<int> RemoverVencidosDaContaAsync(Guid contaId, DateTimeOffset agora, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdempotencyDbContext>();
        var tenantScopeFactory = scope.ServiceProvider.GetRequiredService<ITenantScopeFactory>();

        using var tenantScope = tenantScopeFactory.BeginTenantScope(contaId);

        var totalRemovidoDaConta = 0;
        int removidos;
        do
        {
            removidos = await db.Set<IdempotencyRecordEntity>()
                .Where(r => r.ContaId == contaId && r.ExpiraEm < agora)
                .OrderBy(r => r.ExpiraEm)
                .Take(TamanhoDoLote)
                .ExecuteDeleteAsync(ct);
            totalRemovidoDaConta += removidos;
        }
        while (removidos == TamanhoDoLote);

        return totalRemovidoDaConta;
    }
}
