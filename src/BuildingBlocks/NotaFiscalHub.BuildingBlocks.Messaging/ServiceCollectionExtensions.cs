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
    /// <see cref="IOutboxPublisher"/> scoped, o <see cref="OutboxTypeRegistry{TDbContext}"/> singleton
    /// ISOLADO do módulo (parametrizado por <typeparamref name="TDbContext"/> — nunca compartilhado com
    /// outro módulo mesmo que ambos chamem esta extensão no mesmo <c>IServiceCollection</c>, ex.: o
    /// Worker), o dispatcher e o serviço de retenção como singleton concreto (resolvível diretamente via
    /// <c>GetRequiredService&lt;OutboxDispatcher&lt;TDbContext&gt;&gt;()</c> — necessário para testes
    /// dispararem um ciclo determinístico — <c>AddHostedService&lt;T&gt;()</c> sozinho só exporia o tipo
    /// como <see cref="Microsoft.Extensions.Hosting.IHostedService"/>) registrado também como
    /// <see cref="Microsoft.Extensions.Hosting.IHostedService"/> pela mesma instância singleton.
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

        services.TryAddSingleton<OutboxTypeRegistry<TDbContext>>();
        services.TryAddSingleton<IOutboxMetrics, OutboxMetrics>();

        services.AddScoped<IOutboxPublisher, OutboxPublisher<TDbContext>>();

        services.AddSingleton<OutboxDispatcher<TDbContext>>();
        services.AddHostedService(sp => sp.GetRequiredService<OutboxDispatcher<TDbContext>>());

        services.AddSingleton<RetentionCleanupService<TDbContext>>();
        services.AddHostedService(sp => sp.GetRequiredService<RetentionCleanupService<TDbContext>>());

        return services;
    }

    /// <summary>
    /// Inscreve <typeparamref name="THandler"/> para consumir <typeparamref name="TEvento"/> NO MÓDULO
    /// <typeparamref name="TDbContext"/>: registra a implementação em DI (scoped — resolve dependências
    /// por escopo, ex.: o <c>TDbContext</c> do próprio dispatch) e no
    /// <see cref="OutboxTypeRegistry{TDbContext}"/> desse módulo especificamente — nunca no de outro
    /// módulo que porventura compartilhe o mesmo <c>IServiceCollection</c>. Múltiplos handlers para o
    /// mesmo evento são suportados (cada um deduplicado independentemente na Inbox).
    /// </summary>
    public static IServiceCollection AddInboxHandler<TDbContext, TEvento, THandler>(this IServiceCollection services)
        where TDbContext : DbContext
        where TEvento : EventoIntegracao
        where THandler : class, IInboxHandler<TEvento>
    {
        services.AddScoped<THandler>();

        services.AddSingleton<IStartupRegistroDeHandler<TDbContext>>(
            new StartupRegistroDeHandler<TDbContext, TEvento, THandler>());

        return services;
    }

    /// <summary>
    /// Declara <typeparamref name="TEvento"/> como evento de PLATAFORMA do módulo
    /// <typeparamref name="TDbContext"/> — o único tipo autorizado a ser publicado com <c>ContaId = null</c>
    /// (spec B3 passo 4) NESSE módulo. Uso restrito a eventos de infraestrutura sem tenant (ex.:
    /// manutenção agendada); todo registro deve vir acompanhado de um comentário no call-site justificando
    /// a ausência de tenant.
    /// </summary>
    public static IServiceCollection AddEventoDePlataforma<TDbContext, TEvento>(this IServiceCollection services)
        where TDbContext : DbContext
        where TEvento : EventoIntegracao
    {
        services.AddSingleton<IStartupRegistroDeHandler<TDbContext>>(
            new StartupRegistroDeEventoDePlataforma<TDbContext, TEvento>());

        return services;
    }
}
