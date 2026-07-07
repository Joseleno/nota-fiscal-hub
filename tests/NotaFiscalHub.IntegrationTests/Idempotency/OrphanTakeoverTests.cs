using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotaFiscalHub.Api.Testing;
using NotaFiscalHub.BuildingBlocks.Idempotency;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;

namespace NotaFiscalHub.IntegrationTests.Idempotency;

/// <summary>
/// Critério de aceite 11 (spec B4): registro <c>EmProcessamento</c> mais velho que <c>OrphanTimeout</c>
/// (relógio injetado via <see cref="IdempotencyTestFixture.Relogio"/>) é reaproveitado (takeover atômico)
/// pelo próximo request com a mesma key — o handler executa de novo. Sob concorrência pós-crash, exatamente
/// um vence o takeover.
///
/// NÃO EXECUTA neste ambiente: requer PostgreSQL real via Testcontainers/Docker, indisponível aqui (ver
/// task-4-report.md, "Gap de ambiente"). Escrito e revisado para rodar em CI/ambiente com Docker — o
/// takeover concorrente é, junto com <see cref="ConcurrencyTests"/>, o teste mais crítico da tarefa.
/// </summary>
public class OrphanTakeoverTests : IClassFixture<IdempotencyTestFixture>
{
    private readonly IdempotencyTestFixture _fixture;

    public OrphanTakeoverTests(IdempotencyTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task RegistroOrfaoMaisVelhoQueOrphanTimeout_NovoRequestFazTakeover()
    {
        using var factory = _fixture.CriarFactory();
        var contador = factory.Services.GetRequiredService<ContadorDoHandlerFake>();
        contador.Resetar();

        var contaId = Guid.NewGuid();
        await PlantarRegistroEmProcessamentoAsync(
            factory, contaId, "orfa", criadaEm: _fixture.Relogio.GetUtcNow() - TimeSpan.FromSeconds(61));

        _fixture.Relogio.Advance(TimeSpan.FromSeconds(1));

        using var cliente = IdempotencyTestFixture.ClienteComTenant(factory, contaId);
        cliente.DefaultRequestHeaders.Add("Idempotency-Key", "orfa");
        var resposta = await cliente.PostAsync("/v1/nfce", JsonContent.Create(new { valor = 1 }));

        Assert.Equal(1, contador.Total);
        Assert.NotEqual(HttpStatusCode.Conflict, resposta.StatusCode);
    }

    [Fact]
    public async Task TakeoverConcorrente_ApenasUmVence()
    {
        using var factory = _fixture.CriarFactory();
        var contador = factory.Services.GetRequiredService<ContadorDoHandlerFake>();
        contador.Resetar();

        var contaId = Guid.NewGuid();
        await PlantarRegistroEmProcessamentoAsync(
            factory, contaId, "orfa2", criadaEm: _fixture.Relogio.GetUtcNow() - TimeSpan.FromSeconds(61));

        var tarefas = Enumerable.Range(0, 4).Select(async _ =>
        {
            using var cliente = IdempotencyTestFixture.ClienteComTenant(factory, contaId);
            cliente.DefaultRequestHeaders.Add("Idempotency-Key", "orfa2");
            return await cliente.PostAsync("/v1/nfce", JsonContent.Create(new { valor = 1 }));
        });

        var respostas = await Task.WhenAll(tarefas);

        Assert.Equal(1, contador.Total);
        Assert.Contains(respostas, r => r.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task RegistroEmProcessamento_AindaDentroDoOrphanTimeout_NaoSofreTakeover_RecebeCorrida()
    {
        // Controle do critério 11 ("antes de vencido o OrphanTimeout, vale o critério 4/corrida"): um
        // registro EmProcessamento recente (não órfão) deve continuar recebendo 409
        // requisicao_em_processamento em vez de takeover.
        using var factory = _fixture.CriarFactory();
        var contador = factory.Services.GetRequiredService<ContadorDoHandlerFake>();
        contador.Resetar();

        var contaId = Guid.NewGuid();
        await PlantarRegistroEmProcessamentoAsync(
            factory, contaId, "recente", criadaEm: _fixture.Relogio.GetUtcNow() - TimeSpan.FromSeconds(5));

        using var cliente = IdempotencyTestFixture.ClienteComTenant(factory, contaId);
        cliente.DefaultRequestHeaders.Add("Idempotency-Key", "recente");
        var resposta = await cliente.PostAsync("/v1/nfce", JsonContent.Create(new { valor = 1 }));

        Assert.Equal(0, contador.Total);
        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.NotNull(resposta.Headers.RetryAfter);
    }

    private static async Task PlantarRegistroEmProcessamentoAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory,
        Guid contaId, string key, DateTimeOffset criadaEm)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdempotencyDbContext>();
        var tenantScopeFactory = scope.ServiceProvider.GetRequiredService<ITenantScopeFactory>();

        using var _ = tenantScopeFactory.BeginTenantScope(contaId);

        db.Set<IdempotencyRecordEntity>().Add(new IdempotencyRecordEntity
        {
            ContaId = contaId,
            Ambiente = "producao",
            Rota = "POST /v1/nfce",
            Key = key,
            // Precisa ser o hash REAL do corpo que os testes enviam (new { valor = 1 }) — um hash
            // arbitrário faz IdempotencyDecisionRules.Decidir ver "hash diferente" e retornar
            // ConflitoHashDiferente em vez de CorridaEmProcessamento/takeover, mascarando o cenário que
            // este helper deveria plantar.
            PayloadHashSha256 = PayloadHasher.Sha256(System.Text.Encoding.UTF8.GetBytes("{\"valor\":1}")),
            Estado = IdempotencyState.EmProcessamento,
            CriadaEm = criadaEm,
            ExpiraEm = criadaEm.AddHours(24),
        });

        await db.SaveChangesAsync();
    }
}
