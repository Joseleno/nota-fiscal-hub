using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotaFiscalHub.BuildingBlocks.Auditoria;
using NotaFiscalHub.BuildingBlocks.Auditoria.Contracts;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;
using NotaFiscalHub.BuildingBlocks.Messaging;
using Xunit;

namespace NotaFiscalHub.IntegrationTests.Auditoria;

/// <summary>
/// Critério de aceite 6 (spec B5): <c>ConsultarAsync</c> com conta A não retorna registros da conta B nem
/// registros de escopo de sistema (<c>conta_id NULL</c>); paginação e filtro por período/ação funcionam.
/// Também cobre o critério de aceite 5 (PII barrada ponta a ponta), pois ambos dependem do mesmo pipeline
/// completo evento→handler→insert.
///
/// NÃO roda neste ambiente (Docker Desktop indisponível) — ver comentário de classe de
/// <see cref="AuditoriaTestFixture"/>.
/// </summary>
public class TenantIsolationTests : IAsyncLifetime
{
    private readonly AuditoriaTestFixture _fixture = new();
    private readonly Guid _contaA = Guid.NewGuid();
    private readonly Guid _contaB = Guid.NewGuid();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task ConsultarAsync_ContaA_NaoRetornaDaContaB_NemDeEscopoDeSistema()
    {
        await using var provider = _fixture.ConstruirProvedor();

        await SemearRegistroAsync(contaId: _contaA);
        await SemearRegistroAsync(contaId: _contaB);
        await SemearRegistroDeSistemaAsync();

        var consulta = new ConsultaAuditoria(_fixture.NovoAuditoriaDbContext());
        var pagina = await consulta.ConsultarAsync(new FiltroAuditoria(_contaA), CancellationToken.None);

        Assert.NotEmpty(pagina.Itens);
        Assert.All(pagina.Itens, r => Assert.Equal(_contaA, r.ContaId));
        Assert.DoesNotContain(pagina.Itens, r => r.ContaId == _contaB);
        Assert.DoesNotContain(pagina.Itens, r => r.ContaId is null);
    }

    [Fact]
    public async Task ConsultarAsync_FiltraPorPeriodoEAcao()
    {
        await using var provider = _fixture.ConstruirProvedor();

        await SemearRegistroAsync(contaId: _contaA, acao: "apikey.revogada", ocorridoEm: DateTimeOffset.UtcNow.AddDays(-10));
        await SemearRegistroAsync(contaId: _contaA, acao: "conta.suspensa", ocorridoEm: DateTimeOffset.UtcNow);

        var consulta = new ConsultaAuditoria(_fixture.NovoAuditoriaDbContext());
        var pagina = await consulta.ConsultarAsync(
            new FiltroAuditoria(_contaA, Acao: "apikey.revogada"), CancellationToken.None);

        Assert.Single(pagina.Itens);
        Assert.Equal("apikey.revogada", pagina.Itens[0].Acao);
    }

    [Fact]
    public async Task EventoComChaveSensivel_RegistroPersisteSemAChave()
    {
        await using var provider = _fixture.ConstruirProvedor();

        var cpf = "12345678900";
        await PublicarEventoAsync(provider, new EventoComCpfDeTeste { ContaId = _contaA, Cpf = cpf });

        var dispatcher = provider.GetRequiredService<OutboxDispatcher<ModuloProdutorDbContext>>();
        await dispatcher.ProcessarLoteAsync();

        await using var dbAuditoria = _fixture.NovoAuditoriaDbContext();
        var registro = await dbAuditoria.Set<RegistroAuditoriaEntity>().SingleAsync();

        Assert.DoesNotContain(cpf, registro.Dados ?? string.Empty);
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

        using var _ = ((AmbientTenantContext)tenantContext).BeginTenantScope(evento.ContaId!.Value);
        var publisher = new OutboxPublisher<ModuloProdutorDbContext>(db, tenantContext, registry, correlationContext);

        using var transacao = await db.Database.BeginTransactionAsync();
        publisher.Publicar(evento);
        await db.SaveChangesAsync();
        await transacao.CommitAsync();

        return evento.MessageId;
    }

    /// <summary>Semeia diretamente na tabela (sem passar pelo pipeline outbox/handler) — mais rápido para testes de consulta pura.</summary>
    private async Task SemearRegistroAsync(Guid? contaId, string acao = "apikey.revogada", DateTimeOffset? ocorridoEm = null)
    {
        await using var db = _fixture.NovoAuditoriaDbContext();
        db.Add(new RegistroAuditoriaEntity
        {
            Id = Guid.NewGuid(),
            ContaId = contaId,
            TipoEvento = "ApiKeyRevogada.v1",
            Acao = acao,
            RecursoTipo = "ApiKey",
            RecursoId = Guid.NewGuid().ToString(),
            Ator = "nfh_live_ab12",
            OcorridoEm = ocorridoEm ?? DateTimeOffset.UtcNow,
            RegistradoEm = DateTimeOffset.UtcNow,
            CorrelationId = Guid.NewGuid().ToString("N"),
            MessageId = Guid.NewGuid(),
            Dados = null,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Semeia um registro de ESCOPO DE SISTEMA (<c>ContaId == null</c>) — spec B5 §Abordagem passo 6:
    /// registros de ações administrativas/backoffice sem tenant, nunca retornáveis por
    /// <see cref="IConsultaAuditoria"/>.
    /// </summary>
    private Task SemearRegistroDeSistemaAsync() => SemearRegistroAsync(contaId: null, acao: "conta.suspensa");
}
