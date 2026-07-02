using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Infrastructure.Persistence;

namespace VisuFiscalHub.Tests.Infrastructure.Persistence;

/// <summary>
/// Testa DomainEventsInterceptor usando um DbContext InMemory mínimo com um aggregate stub.
/// Evita a configuração complexa de DocumentoFiscal (OwnsMany, ValueConverters) que exige
/// PostgreSQL — o comportamento do interceptor não depende do tipo concreto do aggregate.
/// </summary>
public class DomainEventsInterceptorTests : IDisposable
{
    private readonly StubDbContext _dbContext;

    public DomainEventsInterceptorTests()
    {
        var interceptor = new DomainEventsInterceptor();
        var options = new DbContextOptionsBuilder<StubDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(interceptor)
            .Options;
        _dbContext = new StubDbContext(options);
    }

    public void Dispose()
    {
        CorrelationIdAmbient.Set(null);
        _dbContext.Dispose();
    }

    // ── CorrelationId propagado do ambient ───────────────────────────────────────

    [Fact]
    public async Task SavingChanges_ComAmbientDefinido_OutboxMessageRecebeCorrelationId()
    {
        CorrelationIdAmbient.Set("req-abc-123");
        var aggregate = new StubAggregate();
        aggregate.RaisarEvento();
        _dbContext.Aggregates.Add(aggregate);

        await _dbContext.SaveChangesAsync();

        var messages = await _dbContext.Set<OutboxMessage>().ToListAsync();
        messages.ShouldNotBeEmpty();
        messages.ShouldAllBe(m => m.CorrelationId == "req-abc-123");
    }

    [Fact]
    public async Task SavingChanges_SemAmbientDefinido_OutboxMessageComCorrelationIdNulo()
    {
        CorrelationIdAmbient.Set(null);
        var aggregate = new StubAggregate();
        aggregate.RaisarEvento();
        _dbContext.Aggregates.Add(aggregate);

        await _dbContext.SaveChangesAsync();

        var messages = await _dbContext.Set<OutboxMessage>().ToListAsync();
        messages.ShouldNotBeEmpty();
        messages.ShouldAllBe(m => m.CorrelationId == null);
    }

    [Fact]
    public async Task SavingChanges_MultiplosDomainEvents_TodosRecebemMesmoCorrelationId()
    {
        CorrelationIdAmbient.Set("req-multi-999");
        var aggregate = new StubAggregate();
        aggregate.RaisarEvento();
        aggregate.RaisarEvento();
        aggregate.RaisarEvento();
        _dbContext.Aggregates.Add(aggregate);

        await _dbContext.SaveChangesAsync();

        var messages = await _dbContext.Set<OutboxMessage>().ToListAsync();
        messages.Count.ShouldBe(3);
        messages.ShouldAllBe(m => m.CorrelationId == "req-multi-999");
    }

    [Fact]
    public async Task SavingChanges_MultiplosAggregates_OutboxMessagesDeCadaUmRecebemMesmoCorrelationId()
    {
        CorrelationIdAmbient.Set("req-multi-agg");
        var agg1 = new StubAggregate();
        agg1.RaisarEvento();
        var agg2 = new StubAggregate();
        agg2.RaisarEvento();
        agg2.RaisarEvento();
        _dbContext.Aggregates.AddRange(agg1, agg2);

        await _dbContext.SaveChangesAsync();

        var messages = await _dbContext.Set<OutboxMessage>().ToListAsync();
        messages.Count.ShouldBe(3);
        messages.ShouldAllBe(m => m.CorrelationId == "req-multi-agg");
    }

    // ── entidade sem domain events não gera OutboxMessage ────────────────────────

    [Fact]
    public async Task SavingChanges_AggregateSemDomainEvents_NenhumOutboxMessageCriado()
    {
        CorrelationIdAmbient.Set("req-irrelevante");
        var aggregate = new StubAggregate(); // sem RaisarEvento()
        _dbContext.Aggregates.Add(aggregate);

        await _dbContext.SaveChangesAsync();

        var messages = await _dbContext.Set<OutboxMessage>().ToListAsync();
        messages.ShouldBeEmpty();
    }

    // ── domain events limpos somente após SavedChanges ────────────────────────────

    [Fact]
    public async Task SavedChanges_DomainEventsLimposAposCommit()
    {
        var aggregate = new StubAggregate();
        aggregate.RaisarEvento();
        aggregate.RaisarEvento();
        _dbContext.Aggregates.Add(aggregate);

        aggregate.DomainEvents.Count.ShouldBe(2);

        await _dbContext.SaveChangesAsync();

        aggregate.DomainEvents.ShouldBeEmpty();
    }

    // ── payload e tipo corretos ──────────────────────────────────────────────────

    [Fact]
    public async Task SavingChanges_OutboxMessageContemEventTypeEPayloadCorretos()
    {
        var aggregate = new StubAggregate();
        aggregate.RaisarEvento();
        _dbContext.Aggregates.Add(aggregate);

        await _dbContext.SaveChangesAsync();

        var message = (await _dbContext.Set<OutboxMessage>().ToListAsync()).Single();
        message.EventType.ShouldBe(typeof(StubDomainEvent).FullName);
        message.Payload.ShouldNotBeNullOrEmpty();
        message.Id.ShouldNotBe(Guid.Empty);
        message.OccurredAt.ShouldNotBe(default);
    }

    // ── stubs ────────────────────────────────────────────────────────────────────

    private sealed class StubDomainEvent : IDomainEvent
    {
        public Guid EventId { get; } = Guid.NewGuid();
        public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
    }

    private sealed class StubAggregate : Entity<Guid>
    {
        public StubAggregate() : base(Guid.NewGuid()) { }

        public void RaisarEvento() => AddDomainEvent(new StubDomainEvent());
    }

    private sealed class StubDbContext(DbContextOptions<StubDbContext> options) : DbContext(options)
    {
        public DbSet<StubAggregate> Aggregates => Set<StubAggregate>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<StubAggregate>().Ignore(a => a.DomainEvents);
            modelBuilder.Entity<OutboxMessage>();
        }
    }
}
