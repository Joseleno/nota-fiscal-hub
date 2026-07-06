using Microsoft.EntityFrameworkCore;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;
using NotaFiscalHub.BuildingBlocks.Messaging;
using NotaFiscalHub.BuildingBlocks.Observability;
using Xunit;

namespace NotaFiscalHub.IntegrationTests.Messaging;

/// <summary>
/// Critério de aceite 1 (spec B3): publicar um evento é atômico com o <c>SaveChanges</c> do agregado —
/// rollback da transação não deixa linha na Outbox; commit deixa exatamente uma.
/// </summary>
public class OutboxTransactionalityTests : IAsyncLifetime
{
    private readonly MessagingTestFixture _fixture = new();
    private readonly Guid _contaId = Guid.NewGuid();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task Publicar_DentroDaTransacaoDoAgregado_RollbackNaoDeixaLinha()
    {
        var tenantContext = new AmbientTenantContext();
        using var _ = tenantContext.BeginTenantScope(_contaId);
        await using var db = _fixture.NovoDbContext(tenantContext);

        var registry = new OutboxTypeRegistry<MensageriaDbContext>();
        var correlationContext = new CorrelationContext();
        using var escopoDeCorrelacao = correlationContext.Definir("corr-teste-integracao");
        var publisher = new OutboxPublisher<MensageriaDbContext>(db, tenantContext, registry, correlationContext);

        using var transacao = await db.Database.BeginTransactionAsync();
        publisher.Publicar(new EventoDeTeste { ContaId = _contaId, Rotulo = "rollback" });
        await db.SaveChangesAsync();
        await transacao.RollbackAsync();

        await using var dbVerificacao = _fixture.NovoDbContext(tenantContext);
        Assert.Empty(await dbVerificacao.Set<OutboxMessage>().ToListAsync());
    }

    [Fact]
    public async Task Publicar_Commit_ExatamenteUmaLinha()
    {
        var tenantContext = new AmbientTenantContext();
        using var _ = tenantContext.BeginTenantScope(_contaId);
        await using var db = _fixture.NovoDbContext(tenantContext);

        var registry = new OutboxTypeRegistry<MensageriaDbContext>();
        var correlationContext = new CorrelationContext();
        using var escopoDeCorrelacao = correlationContext.Definir("corr-teste-integracao");
        var publisher = new OutboxPublisher<MensageriaDbContext>(db, tenantContext, registry, correlationContext);

        using var transacao = await db.Database.BeginTransactionAsync();
        publisher.Publicar(new EventoDeTeste { ContaId = _contaId, Rotulo = "commit" });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();

        await using var dbVerificacao = _fixture.NovoDbContext(tenantContext);
        Assert.Single(await dbVerificacao.Set<OutboxMessage>().ToListAsync());
    }

    [Fact]
    public async Task Publicar_SemTransacaoAtiva_Lanca()
    {
        var tenantContext = new AmbientTenantContext();
        using var _ = tenantContext.BeginTenantScope(_contaId);
        await using var db = _fixture.NovoDbContext(tenantContext);

        var registry = new OutboxTypeRegistry<MensageriaDbContext>();
        var correlationContext = new CorrelationContext();
        using var escopoDeCorrelacao = correlationContext.Definir("corr-teste-integracao");
        var publisher = new OutboxPublisher<MensageriaDbContext>(db, tenantContext, registry, correlationContext);

        Assert.Throws<InvalidOperationException>(() =>
            publisher.Publicar(new EventoDeTeste { ContaId = _contaId, Rotulo = "sem-transacao" }));
    }
}
