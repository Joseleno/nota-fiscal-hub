using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Query;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;

namespace NotaFiscalHub.BuildingBlocks.Persistence;

/// <summary>
/// Base para todo <c>DbContext</c> de módulo que tenha entidades tenant-scoped. Substitui
/// <see cref="ModuleDbContext"/> como classe-mãe efetiva: registra, para cada tipo de entidade que
/// implementa <see cref="ITenantScopedEntity"/>, um query filter NOMEADO ("Tenant" — EF Core 10) que
/// compara <c>e.ContaId</c> ao tenant ambiente, e o <see cref="TenantWriteInterceptor"/> que estampa/
/// valida <c>ContaId</c> em escrita.
///
/// Fail-closed: <see cref="ITenantContext.ContaId"/> lança <see cref="TenantNaoResolvidoException"/>
/// quando não há escopo ativo, e essa exceção é lançada exatamente na avaliação do filtro — ou seja,
/// qualquer query em entidade tenant-scoped sem escopo ativo lança em vez de retornar linhas (nunca
/// "tudo" nem "nada" silenciosamente).
///
/// O nome "Tenant" do filtro é o que permite bypass seletivo via
/// <c>IgnoreQueryFilters(["Tenant"])</c> (uso restrito ao kernel) e é o que o teste de arquitetura
/// (NetArchTest, Tarefa 6) inspeciona por nome para garantir que todo DbContext concreto o registrou.
/// </summary>
public abstract class TenantDbContext(DbContextOptions options, ITenantContext tenantContext) : ModuleDbContext(options)
{
    /// <summary>
    /// Identidade da instância de <see cref="ITenantContext"/> usada para compilar o model deste
    /// <c>DbContext</c>. Consumida por <see cref="TenantModelCacheKeyFactory"/> para que o cache de model
    /// do EF Core (por padrão, por TIPO de DbContext) não reuse um query filter compilado a partir de
    /// uma instância de tenant context diferente da atual.
    /// </summary>
    internal ITenantContext TenantContextIdentity => tenantContext;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantScopedEntity).IsAssignableFrom(entityType.ClrType)) continue;

            modelBuilder.Entity(entityType.ClrType)
                .HasQueryFilter("Tenant", BuildTenantFilter(entityType.ClrType))
                .Property(nameof(ITenantScopedEntity.ContaId)).HasColumnName("conta_id").IsRequired();
        }
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.AddInterceptors(new TenantWriteInterceptor(tenantContext));
        optionsBuilder.ReplaceService<IEvaluatableExpressionFilter, TenantFilterEvaluatableExpressionFilter>();
        optionsBuilder.ReplaceService<IModelCacheKeyFactory, TenantModelCacheKeyFactory>();
    }

    /// <summary>
    /// Monta a expressão lambda <c>e => tenantContext.ContaId == e.ContaId</c> para o tipo concreto.
    /// A leitura de <c>tenantContext.ContaId</c> acontece a cada avaliação do filtro (fechamento sobre
    /// a instância ambiente) — sem escopo ativo, essa leitura já lança <see cref="TenantNaoResolvidoException"/>
    /// antes de qualquer SQL ser gerado/executado: é isso que torna o comportamento fail-closed.
    /// </summary>
    private LambdaExpression BuildTenantFilter(Type entityClrType)
    {
        var parametro = Expression.Parameter(entityClrType, "e");
        var contaIdDaEntidade = Expression.Property(parametro, nameof(ITenantScopedEntity.ContaId));

        var tenantContextConstante = Expression.Constant(tenantContext);
        var contaIdDoContexto = Expression.Property(tenantContextConstante, nameof(ITenantContext.ContaId));

        var igualdade = Expression.Equal(contaIdDoContexto, contaIdDaEntidade);
        return Expression.Lambda(igualdade, parametro);
    }
}
