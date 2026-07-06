using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NotaFiscalHub.BuildingBlocks.Observability;
using Serilog;
using Serilog.Sinks.InMemory;
using Testcontainers.PostgreSql;

namespace NotaFiscalHub.IntegrationTests.Observability;

/// <summary>
/// T3 (spec B7) — guardrail PII ponta a ponta: uma requisição com CPF/nome de consumidor no payload (incl.
/// o caminho de log de EXCEÇÃO, que também carrega o payload no contexto — ver
/// <c>/teste/emissao-fake</c> em Program.cs) não pode produzir NENHUM evento de log com CPF, nome-sentinela
/// ou fragmento de XML fiscal, verificado por <see cref="PiiLogAssertions.AssertNoPii"/> sobre um sink em
/// memória plugado no MESMO pipeline Serilog do host real (<c>AddNfhObservability</c>).
///
/// NÃO EXECUTA neste ambiente: requer PostgreSQL real via Testcontainers/Docker para o host completo subir
/// (IdempotencyDbContext/AuditoriaDbContext são registrados incondicionalmente em Program.cs), indisponível
/// aqui (mesma lacuna documentada nas Tarefas 1-6).
/// </summary>
public class PiiGuardrailTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    public async Task InitializeAsync() => await _postgres.StartAsync();
    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task RequisicaoComCpfNoPayload_NenhumLogContemCpfOuXml()
    {
        // Sink LOCAL (não InMemorySink.Instance/Log.Logger estáticos) — este teste roda em paralelo com
        // outros da mesma suíte (xUnit paraleliza por classe por padrão); mutar Log.Logger estático
        // causaria corrida entre testes que também o substituem (ex. CorrelationIdEndToEndTests).
        var sinkLocal = new InMemorySink();

        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Idempotency", _postgres.GetConnectionString());
            builder.UseSetting("ConnectionStrings:Auditoria", _postgres.GetConnectionString());
            builder.ConfigureServices(services =>
            {
                // Registra um segundo Serilog.ILogger.Logger — a última chamada a AddSerilog vence a
                // resolução de ILoggerFactory no container do WebApplicationFactory — com o MESMO enricher
                // do host real (PiiMaskingEnricher), mas escrevendo no sink local em vez do console.
                services.AddSerilog((_, loggerConfiguration) =>
                {
                    loggerConfiguration
                        .Enrich.FromLogContext()
                        .Enrich.With<PiiMaskingEnricher>()
                        .WriteTo.Sink(sinkLocal);
                });
            });
        });

        using var cliente = factory.CreateClient();
        await cliente.PostAsJsonAsync("/teste/emissao-fake", new { cpf = "12345678900", nome = "Consumidor de Teste" });

        PiiLogAssertions.AssertNoPii(sinkLocal.LogEvents);
    }
}
