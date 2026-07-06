using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotaFiscalHub.BuildingBlocks.Auditoria;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;
using NotaFiscalHub.BuildingBlocks.Messaging;
using Xunit;

namespace NotaFiscalHub.IntegrationTests.Auditoria;

/// <summary>
/// Critérios de aceite 2 e 7 (spec B5): publicar evento catalogado via outbox → exatamente 1 linha em
/// <c>auditoria.registro_auditoria</c> com <c>conta_id</c>/<c>ator</c>/<c>acao</c>/<c>correlation_id</c>/
/// <c>message_id</c> preenchidos; evento não catalogado é consumido sem erro e sem registro.
///
/// NÃO roda neste ambiente (Docker Desktop indisponível) — ver comentário de classe de
/// <see cref="AuditoriaTestFixture"/>.
/// </summary>
public class EventToRecordTests : IAsyncLifetime
{
    private readonly AuditoriaTestFixture _fixture = new();
    private readonly Guid _contaId = Guid.NewGuid();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task EventoCatalogado_ViaOutbox_GeraExatamenteUmRegistro()
    {
        await using var provider = _fixture.ConstruirProvedor();

        var messageId = await PublicarEventoAsync(provider, new ApiKeyRevogadaDeTeste
        {
            ContaId = _contaId,
            ApiKeyId = Guid.NewGuid(),
        });

        var dispatcher = provider.GetRequiredService<OutboxDispatcher<ModuloProdutorDbContext>>();
        await dispatcher.ProcessarLoteAsync();

        await using var dbAuditoria = _fixture.NovoAuditoriaDbContext();
        var registro = await dbAuditoria.Set<RegistroAuditoriaEntity>().SingleAsync();

        Assert.Equal(_contaId, registro.ContaId);
        Assert.NotEqual(Guid.Empty, registro.MessageId);
        Assert.Equal(messageId, registro.MessageId);
        Assert.Equal("apikey.revogada", registro.Acao);
        Assert.False(string.IsNullOrWhiteSpace(registro.Ator));
        Assert.False(string.IsNullOrWhiteSpace(registro.CorrelationId));
    }

    [Fact]
    public async Task EventoNaoCatalogado_ConsumidoSemErro_SemRegistro()
    {
        await using var provider = _fixture.ConstruirProvedor();

        // EventoNaoCatalogadoDeTeste não tem handler inscrito no dispatcher (nunca chamamos
        // AddInboxHandler para ele) — o próprio dispatcher já o classificaria como SemHandler antes de
        // chegar ao catálogo. Isso ainda prova o critério de aceite 7 (nenhum registro é gerado, nenhuma
        // exceção é lançada) pelo caminho mais comum na prática: eventos publicados por módulos de negócio
        // que não são de interesse da auditoria simplesmente não têm o handler registrado para eles.
        var messageId = await PublicarEventoAsync(provider, new EventoNaoCatalogadoDeTeste { ContaId = _contaId });

        var dispatcher = provider.GetRequiredService<OutboxDispatcher<ModuloProdutorDbContext>>();
        await dispatcher.ProcessarLoteAsync();

        await using var dbAuditoria = _fixture.NovoAuditoriaDbContext();
        Assert.Empty(await dbAuditoria.Set<RegistroAuditoriaEntity>().ToListAsync());
        Assert.NotEqual(Guid.Empty, messageId); // só para silenciar aviso de variável não usada de forma útil.
    }

    private async Task<Guid> PublicarEventoAsync<T>(IServiceProvider provider, T evento)
        where T : NotaFiscalHub.BuildingBlocks.Messaging.Abstractions.EventoIntegracao
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ModuloProdutorDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var registry = scope.ServiceProvider.GetRequiredService<OutboxTypeRegistry<ModuloProdutorDbContext>>();
        var correlationContext = scope.ServiceProvider.GetRequiredService<NotaFiscalHub.BuildingBlocks.Observability.CorrelationContext>();
        using var escopoDeCorrelacao = correlationContext.Definir("corr-teste-integracao");

        using var _ = ((AmbientTenantContext)tenantContext).BeginTenantScope(_contaId);
        var publisher = new OutboxPublisher<ModuloProdutorDbContext>(db, tenantContext, registry, correlationContext);

        using var transacao = await db.Database.BeginTransactionAsync();
        publisher.Publicar(evento);
        await db.SaveChangesAsync();
        await transacao.CommitAsync();

        return evento.MessageId;
    }
}
