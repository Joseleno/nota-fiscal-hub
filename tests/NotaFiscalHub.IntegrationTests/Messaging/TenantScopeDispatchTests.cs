using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;
using NotaFiscalHub.BuildingBlocks.Messaging;
using NotaFiscalHub.BuildingBlocks.Messaging.Abstractions;
using Xunit;

namespace NotaFiscalHub.IntegrationTests.Messaging;

/// <summary>
/// Critério de aceite 9 (spec B3) — regra de escopo de tenant no dispatch (design §2.2, "job sem escopo
/// de tenant é erro, não processa tudo"): <c>ContaId = null</c> em tipo NÃO registrado como plataforma
/// vai direto pra <see cref="OutboxStatus.Poison"/> sem executar handler; tipo registrado via
/// <c>AddEventoDePlataforma&lt;T&gt;()</c> executa sem <c>BeginTenantScope</c>.
/// </summary>
public class TenantScopeDispatchTests : IAsyncLifetime
{
    private readonly MessagingTestFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task Dispatcher_ContaIdNulo_TipoNaoRegistradoComoPlataforma_VaiPraPoison()
    {
        var contador = new ContadorDeExecucoes();

        await using var provider = _fixture.ConstruirProvedor(services =>
        {
            services.AddSingleton(contador);
            services.AddInboxHandler<MensageriaDbContext, EventoDeTeste, HandlerQueIncrementa>();
        });

        await PublicarEventoTenantScopedComContaIdNulo(provider);

        var dispatcher = provider.GetRequiredService<OutboxDispatcher<MensageriaDbContext>>();
        await dispatcher.ProcessarLoteAsync();

        await using var db = provider.GetRequiredService<MensageriaDbContext>();
        var mensagem = await db.Set<OutboxMessage>().SingleAsync();

        Assert.Equal(OutboxStatus.Poison, mensagem.Status);
        Assert.Contains("evento tenant-scoped sem conta_id", mensagem.ErroUltimo);

        // Handler NUNCA deve ter executado — "job sem escopo é erro, não processa tudo".
        Assert.Equal(0, contador.Total);
    }

    [Fact]
    public async Task Dispatcher_EventoDePlataforma_ExecutaSemBeginTenantScope()
    {
        var observador = new TenantContextObservado();

        await using var provider = _fixture.ConstruirProvedor(services =>
        {
            services.AddSingleton(observador);
            services.AddScoped<HandlerObservadorDeTenant>();
            services.AddInboxHandler<MensageriaDbContext, EventoDePlataformaDeTeste, HandlerObservadorDeTenant>();
            // Justificativa: evento de teste que representa manutenção/infra agendada sem tenant (spec B3
            // passo 4) — usado apenas para provar que o dispatcher não abre BeginTenantScope neste caso.
            services.AddEventoDePlataforma<MensageriaDbContext, EventoDePlataformaDeTeste>();
        });

        await PublicarEventoDePlataforma(provider);

        var dispatcher = provider.GetRequiredService<OutboxDispatcher<MensageriaDbContext>>();
        await dispatcher.ProcessarLoteAsync();

        Assert.True(observador.Executou);
        Assert.False(observador.HasTenantDuranteExecucao);
    }

    [Fact]
    public async Task Dispatcher_ContaIdPreenchido_EmEventoDePlataforma_VaiPraPoison()
    {
        await using var provider = _fixture.ConstruirProvedor(services =>
        {
            services.AddEventoDePlataforma<MensageriaDbContext, EventoDePlataformaComContaIdDeTeste>();
        });

        var contaId = Guid.NewGuid();
        await PublicarEventoDePlataformaComContaId(provider, contaId);

        var dispatcher = provider.GetRequiredService<OutboxDispatcher<MensageriaDbContext>>();
        await dispatcher.ProcessarLoteAsync();

        await using var db = provider.GetRequiredService<MensageriaDbContext>();
        var mensagem = await db.Set<OutboxMessage>().SingleAsync();

        Assert.Equal(OutboxStatus.Poison, mensagem.Status);
        Assert.Contains("evento de plataforma com conta_id", mensagem.ErroUltimo);
    }

    private async Task PublicarEventoTenantScopedComContaIdNulo(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MensageriaDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var registry = scope.ServiceProvider.GetRequiredService<OutboxTypeRegistry<MensageriaDbContext>>();

        // Escopo de sistema só para permitir a escrita (TenantWriteInterceptor não estampa ContaId em
        // escopo de sistema) — o evento em si é publicado deliberadamente com ContaId = null.
        using var _ = ((AmbientTenantContext)tenantContext).BeginSystemScope("teste", nameof(TenantScopeDispatchTests));
        var publisher = new OutboxPublisher<MensageriaDbContext>(db, tenantContext, registry);

        using var transacao = await db.Database.BeginTransactionAsync();
        publisher.Publicar(new EventoDeTeste { ContaId = null, Rotulo = "sem-conta" });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
    }

    private async Task PublicarEventoDePlataforma(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MensageriaDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var registry = scope.ServiceProvider.GetRequiredService<OutboxTypeRegistry<MensageriaDbContext>>();

        using var _ = ((AmbientTenantContext)tenantContext).BeginSystemScope("teste", nameof(TenantScopeDispatchTests));
        var publisher = new OutboxPublisher<MensageriaDbContext>(db, tenantContext, registry);

        using var transacao = await db.Database.BeginTransactionAsync();
        publisher.Publicar(new EventoDePlataformaDeTeste { ContaId = null });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
    }

    private async Task PublicarEventoDePlataformaComContaId(IServiceProvider provider, Guid contaId)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MensageriaDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var registry = scope.ServiceProvider.GetRequiredService<OutboxTypeRegistry<MensageriaDbContext>>();

        using var _ = ((AmbientTenantContext)tenantContext).BeginTenantScope(contaId);
        var publisher = new OutboxPublisher<MensageriaDbContext>(db, tenantContext, registry);

        using var transacao = await db.Database.BeginTransactionAsync();
        publisher.Publicar(new EventoDePlataformaComContaIdDeTeste { ContaId = contaId });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
    }

    private sealed class HandlerQueIncrementa(ContadorDeExecucoes contador) : IInboxHandler<EventoDeTeste>
    {
        public Task HandleAsync(EventoDeTeste evento, MensagemContexto ctx, CancellationToken ct)
        {
            contador.Incrementar();
            return Task.CompletedTask;
        }
    }

    private sealed class TenantContextObservado
    {
        public bool Executou { get; set; }
        public bool HasTenantDuranteExecucao { get; set; }
    }

    private sealed class HandlerObservadorDeTenant(TenantContextObservado observador, ITenantContext tenantContext)
        : IInboxHandler<EventoDePlataformaDeTeste>
    {
        public Task HandleAsync(EventoDePlataformaDeTeste evento, MensagemContexto ctx, CancellationToken ct)
        {
            observador.Executou = true;
            observador.HasTenantDuranteExecucao = tenantContext.HasTenant;
            return Task.CompletedTask;
        }
    }
}

/// <summary>
/// Segundo tipo de evento de plataforma — usado isoladamente pelo teste de "ContaId preenchido em evento
/// de plataforma", para não conflitar com o registro de <see cref="EventoDePlataformaDeTeste"/> nos
/// demais testes desta classe (cada teste monta seu próprio <see cref="IServiceProvider"/>, mas o tipo
/// precisa ser distinto para deixar a intenção do teste auto-explicativa).
/// </summary>
public sealed record EventoDePlataformaComContaIdDeTeste : EventoIntegracao;
