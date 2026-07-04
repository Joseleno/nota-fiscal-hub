using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NotaFiscalHub.BuildingBlocks.Idempotency;

/// <summary>Extensões de DI consumidas pelos hosts (API) para ligar o middleware de Idempotency-Key.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra <see cref="IIdempotencyStore"/> scoped, <see cref="IdempotencyEndpointFilter"/> e o job de
    /// expiração como <see cref="Microsoft.Extensions.Hosting.BackgroundService"/> (spec B4 §Abordagem
    /// passo 4). NÃO registra <see cref="IdempotencyDbContext"/> — o provider (Npgsql em produção,
    /// InMemory/Testcontainers em teste) é responsabilidade do host, via
    /// <c>services.AddDbContext&lt;IdempotencyDbContext&gt;(o => o.UseNpgsql(...))</c> chamado ANTES desta
    /// extensão, mesmo desenho de <c>AddOutboxInbox&lt;TDbContext&gt;</c> (Tarefa 3) — o kernel não deve
    /// acoplar a um driver de banco específico.
    /// </summary>
    public static IServiceCollection AddIdempotency(
        this IServiceCollection services, Action<IdempotencyOptions>? opcoes = null)
    {
        services.AddOptions<IdempotencyOptions>();
        if (opcoes is not null)
            services.Configure(opcoes);

        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddScoped<IdempotencyEndpointFilter>();

        services.AddHostedService<IdempotencyExpirationJob>();

        return services;
    }
}
