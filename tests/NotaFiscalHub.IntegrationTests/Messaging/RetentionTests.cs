using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;
using NotaFiscalHub.BuildingBlocks.Messaging;
using Xunit;

namespace NotaFiscalHub.IntegrationTests.Messaging;

/// <summary>
/// Critério de aceite 8 (spec B3): <see cref="RetentionCleanupService{TDbContext}"/> remove
/// <see cref="OutboxStatus.Processada"/> com mais de 7 dias, mas preserva <see cref="OutboxStatus.Poison"/>
/// e <see cref="OutboxStatus.SemHandler"/> INDEPENDENTE da idade — apagar por idade recriaria o "evento
/// sumindo em silêncio" (MSG0005) que a biblioteca elimina.
/// </summary>
public class RetentionTests : IAsyncLifetime
{
    private readonly MessagingTestFixture _fixture = new();
    private readonly Guid _contaId = Guid.NewGuid();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task Limpeza_RemoveProcessadaAntiga_PreservaPoisonESemHandlerAntigos()
    {
        await using var provider = _fixture.ConstruirProvedor(_ => { });

        await PlantarMensagemAsync(provider, OutboxStatus.Processada, processadaEm: DateTimeOffset.UtcNow.AddDays(-8));
        await PlantarMensagemAsync(provider, OutboxStatus.Poison, criadaEm: DateTimeOffset.UtcNow.AddDays(-30));
        await PlantarMensagemAsync(provider, OutboxStatus.SemHandler, criadaEm: DateTimeOffset.UtcNow.AddDays(-30));

        var retentionService = provider.GetRequiredService<RetentionCleanupService<MensageriaDbContext>>();
        await retentionService.ExecutarUmCicloAsync();

        await using var db = provider.GetRequiredService<MensageriaDbContext>();
        Assert.Equal(2, await db.Set<OutboxMessage>().CountAsync());
        Assert.DoesNotContain(await db.Set<OutboxMessage>().ToListAsync(), m => m.Status == OutboxStatus.Processada);
    }

    [Fact]
    public async Task Limpeza_PreservaProcessadaRecente()
    {
        await using var provider = _fixture.ConstruirProvedor(_ => { });

        await PlantarMensagemAsync(provider, OutboxStatus.Processada, processadaEm: DateTimeOffset.UtcNow.AddDays(-1));

        var retentionService = provider.GetRequiredService<RetentionCleanupService<MensageriaDbContext>>();
        await retentionService.ExecutarUmCicloAsync();

        await using var db = provider.GetRequiredService<MensageriaDbContext>();
        Assert.Equal(1, await db.Set<OutboxMessage>().CountAsync());
    }

    [Fact]
    public async Task Limpeza_RemoveInboxAntigaPreservaRecente()
    {
        await using var provider = _fixture.ConstruirProvedor(_ => { });

        await PlantarInboxAsync(provider, "handler-antigo", DateTimeOffset.UtcNow.AddDays(-31));
        await PlantarInboxAsync(provider, "handler-recente", DateTimeOffset.UtcNow.AddDays(-1));

        var retentionService = provider.GetRequiredService<RetentionCleanupService<MensageriaDbContext>>();
        await retentionService.ExecutarUmCicloAsync();

        await using var db = provider.GetRequiredService<MensageriaDbContext>();
        var restantes = await db.Set<InboxMessage>().ToListAsync();

        Assert.Single(restantes);
        Assert.Equal("handler-recente", restantes[0].Handler);
    }

    private async Task PlantarMensagemAsync(
        IServiceProvider provider, OutboxStatus status, DateTimeOffset? processadaEm = null, DateTimeOffset? criadaEm = null)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MensageriaDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();

        using var _ = ((AmbientTenantContext)tenantContext).BeginSystemScope("seed-retention", nameof(RetentionTests));

        db.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TipoEvento = "teste.retention.v1",
            Payload = "{}",
            ContaId = _contaId,
            CorrelationId = Guid.NewGuid().ToString("N"),
            OcorridoEm = criadaEm ?? DateTimeOffset.UtcNow,
            Status = status,
            Tentativas = status == OutboxStatus.Poison ? 10 : 0,
            ProximaTentativaEm = DateTimeOffset.UtcNow,
            ProcessadaEm = processadaEm,
            ErroUltimo = status is OutboxStatus.Poison ? "erro plantado pelo teste" : null,
        });

        await db.SaveChangesAsync();
    }

    private async Task PlantarInboxAsync(IServiceProvider provider, string handler, DateTimeOffset processadaEm)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MensageriaDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();

        using var _ = ((AmbientTenantContext)tenantContext).BeginSystemScope("seed-retention", nameof(RetentionTests));

        db.Set<InboxMessage>().Add(new InboxMessage
        {
            MessageId = Guid.NewGuid(),
            Handler = handler,
            TipoEvento = "teste.retention.v1",
            ProcessadaEm = processadaEm,
        });

        await db.SaveChangesAsync();
    }
}
