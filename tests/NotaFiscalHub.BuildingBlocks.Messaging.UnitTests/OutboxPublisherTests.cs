using Microsoft.EntityFrameworkCore;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;
using NotaFiscalHub.BuildingBlocks.Messaging;
using NotaFiscalHub.BuildingBlocks.Messaging.Abstractions;
using NotaFiscalHub.BuildingBlocks.Persistence;

namespace NotaFiscalHub.BuildingBlocks.Messaging.UnitTests;

/// <summary>
/// Testes de unidade para <see cref="OutboxPublisher{TDbContext}"/>. O gate "sem transação ativa" é
/// exercitado diretamente (não depende de semântica transacional real, só de <c>CurrentTransaction ==
/// null</c>, que é verdadeiro por padrão em qualquer provider). A regra de divergência de ContaId é
/// testada isoladamente contra <see cref="OutboxDispatchRules.ContaIdDoEventoDivergeDoTenantAmbiente"/>
/// (sem DB) — o comportamento fim-a-fim de <see cref="OutboxPublisher{TDbContext}.Publicar{T}"/> dentro de
/// uma transação real (incl. rollback/commit) é responsabilidade do Postgres e é coberto por
/// <c>OutboxTransactionalityTests</c>/<c>TenantScopeDispatchTests</c> (Testcontainers).
/// </summary>
public class OutboxPublisherTests
{
    private static readonly Guid ContaId = Guid.NewGuid();

    [Fact]
    public void Publicar_SemTransacaoAtiva_Lanca()
    {
        var tenantContext = new AmbientTenantContext();
        using var _ = tenantContext.BeginTenantScope(ContaId);
        using var db = NovoDbContext(tenantContext);
        var publisher = new OutboxPublisher<TestDbContext>(db, tenantContext, new OutboxTypeRegistry());

        Assert.Throws<InvalidOperationException>(() =>
            publisher.Publicar(new EventoDeTeste { ContaId = ContaId }));
    }

    [Theory]
    [InlineData(true, false, true)]   // conta diferente da ambiente, escopo de negócio ativo -> diverge
    [InlineData(true, true, false)]   // conta diferente da ambiente, mas escopo de SISTEMA -> não valida
    [InlineData(false, false, false)] // sem tenant ambiente ativo -> não valida
    public void ContaIdDoEventoDivergeDoTenantAmbiente_ClassificaCorretamente(
        bool tenantAtivo, bool ehSistema, bool esperaDivergencia)
    {
        var contaAmbiente = Guid.NewGuid();
        var contaDoEvento = Guid.NewGuid(); // sempre diferente de contaAmbiente

        var diverge = OutboxDispatchRules.ContaIdDoEventoDivergeDoTenantAmbiente(
            contaDoEvento, tenantAtivo, ehSistema, contaAmbiente);

        Assert.Equal(esperaDivergencia, diverge);
    }

    [Fact]
    public void ContaIdDoEventoDivergeDoTenantAmbiente_ContaIdNuloNoEvento_NuncaDiverge()
    {
        // Evento de plataforma (ContaId = null) nunca é bloqueado por esta regra — mesmo dentro de um
        // escopo de tenant de negócio ativo.
        var diverge = OutboxDispatchRules.ContaIdDoEventoDivergeDoTenantAmbiente(
            contaIdDoEvento: null, tenantAmbienteAtivo: true, ehEscopoDeSistema: false, contaIdAmbiente: Guid.NewGuid());

        Assert.False(diverge);
    }

    [Fact]
    public void ContaIdDoEventoDivergeDoTenantAmbiente_MesmaConta_NaoDiverge()
    {
        var conta = Guid.NewGuid();

        var diverge = OutboxDispatchRules.ContaIdDoEventoDivergeDoTenantAmbiente(
            contaIdDoEvento: conta, tenantAmbienteAtivo: true, ehEscopoDeSistema: false, contaIdAmbiente: conta);

        Assert.False(diverge);
    }

    private static TestDbContext NovoDbContext(ITenantContext tenantContext)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(options, tenantContext);
    }

    private sealed record EventoDeTeste : EventoIntegracao;

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options, ITenantContext tenantContext)
        : TenantDbContext(options, tenantContext)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.AplicarOutboxInbox("teste_publisher");
        }
    }
}
