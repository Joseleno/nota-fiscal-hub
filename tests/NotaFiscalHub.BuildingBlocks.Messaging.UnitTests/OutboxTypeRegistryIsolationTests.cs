using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;
using NotaFiscalHub.BuildingBlocks.Messaging;
using NotaFiscalHub.BuildingBlocks.Messaging.Abstractions;
using NotaFiscalHub.BuildingBlocks.Persistence;

namespace NotaFiscalHub.BuildingBlocks.Messaging.UnitTests;

/// <summary>
/// Prova de correção do achado CRÍTICO da revisão da Tarefa 3: <c>OutboxTypeRegistry</c> não era
/// parametrizado por <c>TDbContext</c> e era registrado via <c>TryAddSingleton&lt;OutboxTypeRegistry&gt;</c>
/// — quando dois módulos chamam <c>AddOutboxInbox&lt;TDbContext&gt;()</c> no MESMO <c>IServiceCollection</c>
/// (cenário real do <c>NotaFiscalHub.Worker</c>, que referencia todos os módulos de negócio), o SEGUNDO
/// módulo silenciosamente herdava a instância do PRIMEIRO — exatamente o antipadrão de "outbox central"
/// que a spec B3 proíbe ("nunca serviço central"). Este teste constrói UM único <see cref="IServiceCollection"/>
/// com dois módulos (dois <c>TDbContext</c> fake distintos) e prova que cada um resolve um
/// <see cref="OutboxTypeRegistry{TDbContext}"/> isolado — sem vazamento de handlers nem de eventos de
/// plataforma de um módulo para o outro.
/// </summary>
public class OutboxTypeRegistryIsolationTests
{
    [Fact]
    public void DoisModulosNoMesmoServiceCollection_RegistriesIsoladas_SemVazamentoDeHandler()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new AmbientTenantContext());
        services.AddSingleton<ITenantContext>(sp => sp.GetRequiredService<AmbientTenantContext>());
        services.AddSingleton<ITenantScopeFactory>(sp => sp.GetRequiredService<AmbientTenantContext>());

        services.AddDbContext<ModuloADbContext>(o => o.UseInMemoryDatabase("modulo-a"));
        services.AddDbContext<ModuloBDbContext>(o => o.UseInMemoryDatabase("modulo-b"));

        // Módulo A: registra handler para EventoDeA.
        services.AddOutboxInbox<ModuloADbContext>("modulo_a");
        services.AddInboxHandler<ModuloADbContext, EventoDeA, HandlerDeA>();

        // Módulo B: registra handler para EventoDeB e um evento de plataforma próprio.
        services.AddOutboxInbox<ModuloBDbContext>("modulo_b");
        services.AddInboxHandler<ModuloBDbContext, EventoDeB, HandlerDeB>();
        services.AddEventoDePlataforma<ModuloBDbContext, EventoDePlataformaDeB>();

        using var provider = services.BuildServiceProvider();

        var registryA = provider.GetRequiredService<OutboxTypeRegistry<ModuloADbContext>>();
        var registryB = provider.GetRequiredService<OutboxTypeRegistry<ModuloBDbContext>>();

        // As duas instâncias são DISTINTAS — a garantia estrutural mínima de isolamento.
        Assert.NotSame(registryA, registryB);

        // O registry de A só conhece o handler de A — nunca o de B.
        Assert.Single(registryA.HandlersRegistrados(typeof(EventoDeA)));
        Assert.Empty(registryA.HandlersRegistrados(typeof(EventoDeB)));

        // O registry de B só conhece o handler de B — nunca o de A.
        Assert.Single(registryB.HandlersRegistrados(typeof(EventoDeB)));
        Assert.Empty(registryB.HandlersRegistrados(typeof(EventoDeA)));

        // O evento de plataforma registrado em B não "vaza" para o registry de A.
        Assert.True(registryB.EhEventoDePlataforma(typeof(EventoDePlataformaDeB)));
        Assert.False(registryA.EhEventoDePlataforma(typeof(EventoDePlataformaDeB)));
    }

    private sealed record EventoDeA : EventoIntegracao;
    private sealed record EventoDeB : EventoIntegracao;
    private sealed record EventoDePlataformaDeB : EventoIntegracao;

    private sealed class HandlerDeA : IInboxHandler<EventoDeA>
    {
        public Task HandleAsync(EventoDeA evento, MensagemContexto ctx, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class HandlerDeB : IInboxHandler<EventoDeB>
    {
        public Task HandleAsync(EventoDeB evento, MensagemContexto ctx, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class ModuloADbContext(DbContextOptions<ModuloADbContext> options, ITenantContext tenantContext)
        : TenantDbContext(options, tenantContext)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.AplicarOutboxInbox("modulo_a");
        }
    }

    private sealed class ModuloBDbContext(DbContextOptions<ModuloBDbContext> options, ITenantContext tenantContext)
        : TenantDbContext(options, tenantContext)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.AplicarOutboxInbox("modulo_b");
        }
    }
}
