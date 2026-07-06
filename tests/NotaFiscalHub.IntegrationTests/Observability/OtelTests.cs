using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotaFiscalHub.Api.Authentication;
using NotaFiscalHub.BuildingBlocks.Idempotency;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Testcontainers.PostgreSql;

namespace NotaFiscalHub.IntegrationTests.Observability;

/// <summary>
/// T5 (spec B7) — OTel: exporter em memória captura span HTTP e span EF Core/Npgsql; nenhum span de EF
/// carrega VALOR de parâmetro SQL (só o texto do statement, com placeholder — spec B7 passo 4); <c>/health</c>
/// e <c>/alive</c> não geram span (filtro de ruído do AddAspNetCoreInstrumentation).
///
/// Requer PostgreSQL real via Testcontainers/Docker. Docker estava disponível neste ambiente quando esta
/// suíte foi escrita e verificada — os testes rodam de fato e passam contra Postgres real, ao contrário da
/// lacuna documentada nas Tarefas 1-6.
/// </summary>
public class OtelTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private readonly List<System.Diagnostics.Activity> _exportedItems = [];

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // /teste/consulta-com-parametro consulta IdempotencyRecordEntity via IdempotencyDbContext — o
        // schema precisa existir neste Postgres do Testcontainers antes da requisição, senão a query
        // falha por tabela ausente e nenhum span de comando chega a ser emitido corretamente.
        var tenantContext = new AmbientTenantContext();
        using var _ = tenantContext.BeginSystemScope("seed", nameof(OtelTests));
        var options = new DbContextOptionsBuilder<IdempotencyDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options;
        await using var db = new IdempotencyDbContext(options, tenantContext);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private WebApplicationFactory<Program> CriarFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Idempotency", _postgres.GetConnectionString());
            builder.UseSetting("ConnectionStrings:Auditoria", _postgres.GetConnectionString());
            builder.ConfigureServices(services =>
            {
                // Compõe com o TracerProviderBuilder já registrado por AddNfhObservability — chamadas
                // subsequentes de WithTracing acumulam instrumentação/exporters no MESMO provider, não
                // substituem o anterior.
                services.AddOpenTelemetry().WithTracing(tracing => tracing.AddInMemoryExporter(_exportedItems));
            });
        });

    [Fact]
    public async Task OtelExporterEmMemoria_CapturaSpanHttpESpanEf_SemValorDeParametroSql()
    {
        using var factory = CriarFactory();
        using var cliente = factory.CreateClient();
        cliente.DefaultRequestHeaders.Add(StubTenantResolver.HeaderContaId, Guid.NewGuid().ToString());

        await cliente.GetAsync("/teste/consulta-com-parametro?cpf=12345678900");

        // Força o flush do BatchExportProcessor (exporters em memória exportam em lote por padrão).
        var tracerProvider = factory.Services.GetRequiredService<TracerProvider>();
        tracerProvider.ForceFlush();

        var spanEf = _exportedItems.Single(s => s.Source.Name.Contains("Npgsql"));
        Assert.DoesNotContain("12345678900", spanEf.Tags.Select(t => t.Value?.ToString()));
    }

    [Fact]
    public async Task Health_NaoGeraSpan()
    {
        using var factory = CriarFactory();
        using var cliente = factory.CreateClient();

        await cliente.GetAsync("/health");

        var tracerProvider = factory.Services.GetRequiredService<TracerProvider>();
        tracerProvider.ForceFlush();

        Assert.DoesNotContain(_exportedItems, s => s.DisplayName.Contains("/health"));
    }
}
