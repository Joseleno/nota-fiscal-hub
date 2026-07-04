using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using NotaFiscalHub.Api.Authentication;
using NotaFiscalHub.Api.Testing;
using NotaFiscalHub.BuildingBlocks.Idempotency;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;
using Testcontainers.PostgreSql;

namespace NotaFiscalHub.IntegrationTests.Idempotency;

/// <summary>
/// Fixture compartilhada dos testes de integração de Idempotency-Key (Tarefa 4, spec B4): sobe um
/// PostgreSQL real via Testcontainers, substitui o <see cref="IdempotencyDbContext"/> registrado por
/// <c>Program.cs</c> para apontar para esse Postgres, aplica o schema via <c>EnsureCreatedAsync</c> (mesmo
/// padrão de <see cref="NotaFiscalHub.IntegrationTests.Messaging.MessagingTestFixture"/>, Tarefa 3) e troca
/// o <see cref="TimeProvider"/> real por um <see cref="FakeTimeProvider"/> injetável — necessário pelos
/// testes de expiração/órfão (spec B4 §Abordagem passos 4/5), que precisam avançar o relógio
/// deterministicamente sem <c>Task.Delay</c> real.
///
/// NÃO roda neste ambiente de desenvolvimento (Docker Desktop indisponível — mesma lacuna já documentada
/// nas Tarefas 1/2/3/6, ver task-4-report.md): a fixture falha em <see cref="InitializeAsync"/> na
/// construção/start do <see cref="PostgreSqlContainer"/>.
/// </summary>
public sealed class IdempotencyTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public FakeTimeProvider Relogio { get; } = new(DateTimeOffset.UtcNow);

    public string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var tenantContext = new AmbientTenantContext();
        using var _ = tenantContext.BeginSystemScope("seed", nameof(IdempotencyTestFixture));

        var options = new DbContextOptionsBuilder<IdempotencyDbContext>().UseNpgsql(ConnectionString).Options;
        await using var db = new IdempotencyDbContext(options, tenantContext);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    /// <summary>
    /// Monta um <see cref="WebApplicationFactory{TEntryPoint}"/> do host real (<c>Api</c>) com o
    /// <see cref="IdempotencyDbContext"/> repontado para o Postgres do Testcontainers e o
    /// <see cref="TimeProvider"/> trocado pelo <see cref="Relogio"/> injetável.
    /// </summary>
    public WebApplicationFactory<Program> CriarFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<IdempotencyDbContext>>();
                services.AddDbContext<IdempotencyDbContext>(o => o.UseNpgsql(ConnectionString));

                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Relogio);
            });
        });

    public static HttpClient ClienteComTenant(WebApplicationFactory<Program> factory, Guid contaId, string? ambiente = null)
    {
        var cliente = factory.CreateClient();
        cliente.DefaultRequestHeaders.Add(StubTenantResolver.HeaderContaId, contaId.ToString());
        if (ambiente is not null)
            cliente.DefaultRequestHeaders.Add(StubAmbienteMiddleware.HeaderAmbiente, ambiente);
        return cliente;
    }
}
