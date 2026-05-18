using System.Text.Json;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Events;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Infrastructure.Jobs;
using VisuFiscalHub.Infrastructure.Persistence;

namespace VisuFiscalHub.Tests.Infrastructure.Jobs;

/// <summary>
/// Testa o fluxo de ExecuteAsync usando uma subclasse testável que substitui o SELECT
/// PostgreSQL-specific (FOR UPDATE SKIP LOCKED) por uma lista em memória controlada.
/// O ApplicationDbContext usa InMemory provider para suportar transações no fluxo de teste.
/// </summary>
public class OutboxRelayJobExecuteTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly TimeProvider _timeProvider = new FixedTimeProvider(
        new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero));

    public OutboxRelayJobExecuteTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _dbContext = new ApplicationDbContext(options, NullLoggerFactory.Instance);
    }

    public void Dispose() => _dbContext.Dispose();

    private TestableOutboxRelayJob CreateJob(IReadOnlyList<OutboxMessage> messages)
        => new(_dbContext, _publisher, _timeProvider,
            NullLogger<OutboxRelayJob>.Instance, messages);

    // ── batch vazio ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_BatchVazio_NaoPublicaNada()
    {
        var job = CreateJob([]);

        await job.ExecuteAsync();

        await _publisher.DidNotReceiveWithAnyArgs().Publish(default!, default);
    }

    // ── mensagem válida ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_MensagemValida_PublicaEMarcaProcessada()
    {
        var message = BuildMessage(BuildValidEvent());
        var job = CreateJob([message]);

        await job.ExecuteAsync();

        // O job chama _publisher.Publish(object, ct) — overload não-genérico de IPublisher.
        await _publisher.Received(1).Publish(Arg.Any<object>(), Arg.Any<CancellationToken>());
        message.ProcessedAt.ShouldNotBeNull();
    }

    // ── isolamento de falhas entre mensagens ─────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_PublishFalhaEmMensagem1_Mensagem2AindaEhProcessada()
    {
        var msg1 = BuildMessage(BuildValidEvent());
        var msg2 = BuildMessage(BuildValidEvent());

        // Configura o overload não-genérico Publish(object, ct):
        // primeira chamada lança, segunda retorna com sucesso.
        _publisher
            .Publish(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => throw new InvalidOperationException("falha simulada"),
                _ => ValueTask.CompletedTask);

        var job = CreateJob([msg1, msg2]);

        await job.ExecuteAsync();

        // msg1 falhou no Publish — permanece na fila (ProcessedAt nulo)
        msg1.ProcessedAt.ShouldBeNull();
        // msg2 foi processada com sucesso apesar da falha em msg1
        msg2.ProcessedAt.ShouldNotBeNull();
    }

    // ── tipo desconhecido ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_TipoDesconhecido_MarcaProcessadaSemPublicar()
    {
        var message = new OutboxMessage
        {
            Id        = Guid.NewGuid(),
            EventType = "Tipo.Inexistente, AssemblyFantasma",
            Payload   = "{}",
            OccurredAt = DateTimeOffset.UtcNow,
        };
        var job = CreateJob([message]);

        await job.ExecuteAsync();

        await _publisher.DidNotReceiveWithAnyArgs().Publish(default(object)!, default);
        message.ProcessedAt.ShouldNotBeNull();
    }

    // ── payload nulo após desserialização ────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_PayloadNuloAposDeserializacao_MarcaProcessadaSemPublicar()
    {
        // JsonSerializer.Deserialize retorna null para o payload "null" em JSON.
        // O job detecta domainEvent is null e marca a mensagem sem publicar.
        var typeName = typeof(DocumentoFiscalAutorizadoEvent).AssemblyQualifiedName!;
        var message = new OutboxMessage
        {
            Id        = Guid.NewGuid(),
            EventType = typeName,
            Payload   = "null",
            OccurredAt = DateTimeOffset.UtcNow,
        };
        var job = CreateJob([message]);

        await job.ExecuteAsync();

        await _publisher.DidNotReceiveWithAnyArgs().Publish(default(object)!, default);
        message.ProcessedAt.ShouldNotBeNull();
    }

    // ── helpers ──────────────────────────────────────────────────────────────────

    private static IDomainEvent BuildValidEvent() => new DocumentoFiscalAutorizadoEvent(
        DocumentoFiscalId.New(),
        new TenantId(Guid.NewGuid()),
        ClienteAppId.New(),
        ChaveAcesso: "35260112345678000195650010000000011000000015",
        Protocolo:   "315260000000001",
        AuthorizedAt: DateTimeOffset.UtcNow,
        EventId:     Guid.NewGuid(),
        OccurredAt:  DateTimeOffset.UtcNow);

    private static OutboxMessage BuildMessage(IDomainEvent evt) => new()
    {
        Id         = Guid.NewGuid(),
        EventType  = evt.GetType().AssemblyQualifiedName!,
        Payload    = JsonSerializer.Serialize(evt, evt.GetType()),
        OccurredAt = evt.OccurredAt,
    };

    private sealed class FixedTimeProvider(DateTimeOffset fixedNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => fixedNow;
    }

    // Subclasse testável: substitui o SELECT PostgreSQL por uma lista em memória.
    // Único override necessário — todo o fluxo transacional real de ExecuteAsync é exercitado.
    private sealed class TestableOutboxRelayJob(
        ApplicationDbContext dbContext,
        IPublisher publisher,
        TimeProvider timeProvider,
        ILogger<OutboxRelayJob> logger,
        IReadOnlyList<OutboxMessage> messages)
        : OutboxRelayJob(dbContext, publisher, timeProvider, logger)
    {
        protected override Task<List<OutboxMessage>> FetchPendingMessagesAsync(CancellationToken ct)
            => Task.FromResult(messages.ToList());
    }
}
