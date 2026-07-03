using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NotaFiscalHub.BuildingBlocks.Messaging.Abstractions;

namespace NotaFiscalHub.BuildingBlocks.Messaging;

/// <summary>Extensões de DI consumidas pelos hosts (API/Worker) para ligar a biblioteca Outbox/Inbox a um módulo.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra a infraestrutura de Outbox/Inbox para o <typeparamref name="TDbContext"/> de um módulo:
    /// <see cref="IOutboxPublisher"/> scoped, o <see cref="OutboxTypeRegistry"/> singleton do módulo, o
    /// dispatcher e o serviço de retenção como <see cref="Microsoft.Extensions.Hosting.BackgroundService"/>.
    /// <paramref name="schema"/> é só documental aqui (o schema já está fixado no
    /// <c>OnModelCreating</c> do próprio <typeparamref name="TDbContext"/> via <c>AplicarOutboxInbox</c>)
    /// — mantido no parâmetro para deixar explícito, na chamada, qual módulo está sendo ligado.
    /// </summary>
    public static IServiceCollection AddOutboxInbox<TDbContext>(
        this IServiceCollection services, string schema, Action<OutboxInboxOptions>? opcoes = null)
        where TDbContext : DbContext
    {
        _ = schema;

        services.AddOptions<OutboxInboxOptions>();
        if (opcoes is not null)
            services.Configure(opcoes);

        services.TryAddSingleton<OutboxTypeRegistry>();
        services.TryAddSingleton<IOutboxMetrics, OutboxMetrics>();

        services.AddScoped<IOutboxPublisher, OutboxPublisher<TDbContext>>();

        services.AddHostedService<OutboxDispatcher<TDbContext>>();
        services.AddHostedService<RetentionCleanupService<TDbContext>>();

        return services;
    }

    /// <summary>
    /// Inscreve <typeparamref name="THandler"/> para consumir <typeparamref name="TEvento"/>: registra a
    /// implementação em DI (scoped — resolve dependências por escopo, ex.: o <c>TDbContext</c> do
    /// próprio dispatch) e no <see cref="OutboxTypeRegistry"/> do processo. Múltiplos handlers para o
    /// mesmo evento são suportados (cada um deduplicado independentemente na Inbox).
    /// </summary>
    public static IServiceCollection AddInboxHandler<TEvento, THandler>(this IServiceCollection services)
        where TEvento : EventoIntegracao
        where THandler : class, IInboxHandler<TEvento>
    {
        services.AddScoped<THandler>();

        services.AddSingleton<IStartupRegistroDeHandler>(new StartupRegistroDeHandler<TEvento, THandler>());

        return services;
    }

    /// <summary>
    /// Declara <typeparamref name="TEvento"/> como evento de PLATAFORMA — o único tipo autorizado a ser
    /// publicado com <c>ContaId = null</c> (spec B3 passo 4). Uso restrito a eventos de infraestrutura
    /// sem tenant (ex.: manutenção agendada); todo registro deve vir acompanhado de um comentário no
    /// call-site justificando a ausência de tenant.
    /// </summary>
    public static IServiceCollection AddEventoDePlataforma<TEvento>(this IServiceCollection services)
        where TEvento : EventoIntegracao
    {
        services.AddSingleton<IStartupRegistroDeHandler>(new StartupRegistroDeEventoDePlataforma<TEvento>());

        return services;
    }
}
