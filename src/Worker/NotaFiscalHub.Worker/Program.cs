using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;
using NotaFiscalHub.BuildingBlocks.Observability;
using NotaFiscalHub.Worker;

// Tarefa 7 / spec B7 passo 5: o Worker é um BackgroundService puro (Tarefa 1) sem pipeline HTTP — mas
// precisa expor /alive e /health como a API. Em vez de um segundo processo, sobe-se um HOST HTTP MÍNIMO
// (WebApplication) só para esses 2 endpoints, no mesmo processo: WebApplication.CreateBuilder ainda
// registra IHostedService/BackgroundService normalmente (é um IHostApplicationBuilder como outro
// qualquer), então nenhum comportamento do Worker muda — só ganha uma porta de management.
var builder = WebApplication.CreateBuilder(args);

builder.Configuration[ObservabilityExtensions.ChaveConfigPostgresHealthCheck] =
    builder.Configuration.GetConnectionString("Idempotency")
    ?? "Host=localhost;Database=nota_fiscal_hub_placeholder;Username=postgres;Password=postgres";
builder.AddNfhObservability("nfh-worker");

// Ver comentário equivalente em NotaFiscalHub.Api/Program.cs: singleton é obrigatório para o cache de
// model do EF Core por DbContext funcionar corretamente (TenantModelCacheKeyFactory).
builder.Services.AddSingleton<AmbientTenantContext>();
builder.Services.AddSingleton<ITenantContext>(sp => sp.GetRequiredService<AmbientTenantContext>());
builder.Services.AddSingleton<ITenantScopeFactory>(sp => sp.GetRequiredService<AmbientTenantContext>());
builder.Services.AddSingleton<TenantJobExecutor>();

var app = builder.Build();

app.UseNfhObservability();

app.Run();

// Necessário para que o WebApplicationFactory<Program> dos testes de integração enxergue o entry point
// (mesmo padrão do Api/Program.cs) — usado pelos testes de health check da Tarefa 7.
public partial class Program;
