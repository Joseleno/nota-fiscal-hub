using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotaFiscalHub.BuildingBlocks.Auditoria;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;
using NotaFiscalHub.BuildingBlocks.Messaging;
using Xunit;

namespace NotaFiscalHub.IntegrationTests.Auditoria;

/// <summary>
/// Critério de aceite 4 (spec B5): reentrega da mesma mensagem (mesmo <c>messageId</c>, 2×, inclusive fora
/// de ordem com outra mensagem no meio) → 1 registro, handler não lança. Dedupe em DUAS camadas: a Inbox
/// (Tarefa 3) é a barreira primária; <c>ON CONFLICT (message_id) DO NOTHING</c> no insert do handler é a
/// segunda barreira, específica de <c>auditoria.registro_auditoria</c>.
///
/// NÃO roda neste ambiente (Docker Desktop indisponível) — ver comentário de classe de
/// <see cref="AuditoriaTestFixture"/>.
/// </summary>
public class DedupeTests : IAsyncLifetime
{
    private readonly AuditoriaTestFixture _fixture = new();
    private readonly Guid _contaId = Guid.NewGuid();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task ReentregaMesmaMensagem_ForaDeOrdem_UmRegistroSo()
    {
        await using var provider = _fixture.ConstruirProvedor();

        var evento = new ApiKeyRevogadaDeTeste { ContaId = _contaId, ApiKeyId = Guid.NewGuid() };
        var messageId = await PublicarEventoAsync(provider, evento);

        var dispatcher = provider.GetRequiredService<OutboxDispatcher<ModuloProdutorDbContext>>();

        // 1º despacho: processa e marca Processada (mesmo racional de InboxDedupeTests, Tarefa 3).
        await dispatcher.ProcessarLoteAsync();

        // Simula reentrega fora de ordem: publica um SEGUNDO evento diferente entre as duas tentativas de
        // entrega do primeiro, então força a reentrega do primeiro.
        await PublicarEventoAsync(provider, new ApiKeyRevogadaDeTeste { ContaId = _contaId, ApiKeyId = Guid.NewGuid() });
        await dispatcher.ProcessarLoteAsync();

        await ForcarReentregaAsync(provider, messageId);
        await dispatcher.ProcessarLoteAsync();

        await using var dbAuditoria = _fixture.NovoAuditoriaDbContext();
        var registrosDoEventoOriginal = await dbAuditoria.Set<RegistroAuditoriaEntity>()
            .Where(r => r.MessageId == messageId)
            .ToListAsync();

        Assert.Single(registrosDoEventoOriginal);
    }

    private async Task ForcarReentregaAsync(IServiceProvider provider, Guid messageId)
    {
        // Devolve a mensagem original para Pendente e imediatamente elegível — mesma técnica de
        // InboxDedupeTests (Tarefa 3): simula crash entre commit do handler e marcação como Processada, ou
        // redisparo manual via runbook.
        await using var db = provider.GetRequiredService<ModuloProdutorDbContext>();
        await db.Set<OutboxMessage>()
            .Where(m => m.Id == messageId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, OutboxStatus.Pendente)
                .SetProperty(m => m.ProximaTentativaEm, DateTimeOffset.UtcNow));
    }

    private async Task<Guid> PublicarEventoAsync(IServiceProvider provider, ApiKeyRevogadaDeTeste evento)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ModuloProdutorDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var registry = scope.ServiceProvider.GetRequiredService<OutboxTypeRegistry<ModuloProdutorDbContext>>();

        using var _ = ((AmbientTenantContext)tenantContext).BeginTenantScope(_contaId);
        var publisher = new OutboxPublisher<ModuloProdutorDbContext>(db, tenantContext, registry);

        using var transacao = await db.Database.BeginTransactionAsync();
        publisher.Publicar(evento);
        await db.SaveChangesAsync();
        await transacao.CommitAsync();

        return evento.MessageId;
    }
}
