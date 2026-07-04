using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotaFiscalHub.Api.Testing;
using NotaFiscalHub.BuildingBlocks.Idempotency;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;

namespace NotaFiscalHub.IntegrationTests.Idempotency;

/// <summary>
/// Critérios de aceite 8 e 12 (spec B4): após <c>ExpiraEm</c>, a mesma key + mesmo corpo executa o handler
/// de novo (requisição nova); o job de expiração abre <see cref="ITenantScopeFactory.BeginTenantScope"/>
/// por conta (Global Constraint) e remove só os registros vencidos.
///
/// NÃO EXECUTA neste ambiente: requer PostgreSQL real via Testcontainers/Docker, indisponível aqui (ver
/// task-4-report.md, "Gap de ambiente"). Escrito e revisado para rodar em CI/ambiente com Docker.
/// </summary>
public class ExpirationTests : IClassFixture<IdempotencyTestFixture>
{
    private readonly IdempotencyTestFixture _fixture;

    public ExpirationTests(IdempotencyTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task AposExpiraEm_MesmaKeyExecutaHandlerDeNovo()
    {
        using var factory = _fixture.CriarFactory();
        var contador = factory.Services.GetRequiredService<ContadorDoHandlerFake>();
        contador.Resetar();

        var contaId = Guid.NewGuid();
        await PlantarRegistroConcluidaAsync(
            factory, contaId, "expirada", expiraEm: _fixture.Relogio.GetUtcNow() - TimeSpan.FromMinutes(1));

        using var cliente = IdempotencyTestFixture.ClienteComTenant(factory, contaId);
        cliente.DefaultRequestHeaders.Add("Idempotency-Key", "expirada");
        var resposta = await cliente.PostAsync("/v1/nfce", JsonContent.Create(new { valor = 1 }));

        Assert.Equal(1, contador.Total);
    }

    [Fact]
    public async Task JobDeExpiracao_AbreBeginTenantScopePorConta_RemoveSoVencidos()
    {
        using var factory = _fixture.CriarFactory();
        var contaA = Guid.NewGuid();

        await PlantarRegistroConcluidaAsync(
            factory, contaA, "vencida", expiraEm: _fixture.Relogio.GetUtcNow() - TimeSpan.FromDays(1));
        await PlantarRegistroConcluidaAsync(
            factory, contaA, "vigente", expiraEm: _fixture.Relogio.GetUtcNow() + TimeSpan.FromDays(1));

        using var scope = factory.Services.CreateScope();
        var job = ActivatorUtilities.CreateInstance<IdempotencyExpirationJob>(scope.ServiceProvider);
        await job.ExecutarUmCicloAsync();

        using var scopeDeLeitura = factory.Services.CreateScope();
        var db = scopeDeLeitura.ServiceProvider.GetRequiredService<IdempotencyDbContext>();
        var tenantScopeFactory = scopeDeLeitura.ServiceProvider.GetRequiredService<ITenantScopeFactory>();
        using var _ = tenantScopeFactory.BeginTenantScope(contaA);

        var restantes = await db.Set<IdempotencyRecordEntity>().Where(r => r.ContaId == contaA).ToListAsync();

        Assert.Single(restantes);
        Assert.Equal("vigente", restantes[0].Key);
    }

    private async Task PlantarRegistroConcluidaAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory,
        Guid contaId, string key, DateTimeOffset expiraEm)
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
            PayloadHashSha256 = PayloadHasher.Sha256(System.Text.Encoding.UTF8.GetBytes("{\"valor\":1}")),
            Estado = IdempotencyState.Concluida,
            RespostaStatus = 201,
            RespostaCorpo = "{}",
            RespostaContentType = "application/json",
            CriadaEm = _fixture.Relogio.GetUtcNow() - TimeSpan.FromHours(25),
            ExpiraEm = expiraEm,
        });

        await db.SaveChangesAsync();
    }
}
