using Microsoft.EntityFrameworkCore;
using NotaFiscalHub.Api.Authentication;
using NotaFiscalHub.Api.Testing;
using NotaFiscalHub.BuildingBlocks.Idempotency;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;

var builder = WebApplication.CreateBuilder(args);

// ITenantContext/ITenantScopeFactory são singleton: o mesmo objeto ambiente serve toda a aplicação,
// variando por AsyncLocal internamente (por request/fluxo assíncrono). Registrar como Scoped criaria
// uma instância por request, o que quebraria o cache de model do EF Core por DbContext (cada instância
// de ITenantContext produziria um model compilado próprio — ver TenantModelCacheKeyFactory).
builder.Services.AddSingleton<AmbientTenantContext>();
builder.Services.AddSingleton<ITenantContext>(sp => sp.GetRequiredService<AmbientTenantContext>());
builder.Services.AddSingleton<ITenantScopeFactory>(sp => sp.GetRequiredService<AmbientTenantContext>());
builder.Services.AddSingleton<ITenantResolver, StubTenantResolver>();

// Idempotency-Key (Tarefa 4/spec B4): IdempotencyDbContext é registrado pelo HOST (não pelo kernel — ver
// AddIdempotency) porque é o host que decide o provider (Npgsql aqui; Testcontainers/InMemory em teste).
var idempotencyConnectionString = builder.Configuration.GetConnectionString("Idempotency")
    ?? "Host=localhost;Database=nota_fiscal_hub_placeholder;Username=postgres;Password=postgres";
builder.Services.AddDbContext<IdempotencyDbContext>(o => o.UseNpgsql(idempotencyConnectionString));
builder.Services.AddIdempotency();

builder.Services.AddSingleton<ContadorDoHandlerFake>();

var app = builder.Build();

app.MapGet("/alive", () => Results.Ok());
app.MapGet("/health", () => Results.Ok());

app.UseMiddleware<TenantResolutionMiddleware>();
app.UseMiddleware<StubAmbienteMiddleware>();

if (app.Environment.IsDevelopment())
{
    // Endpoint de diagnóstico usado pelo teste de integração de concorrência (2 requests não vazam
    // AsyncLocal entre si). Não existe fora de Development. O delay antes de ler ContaId amplia
    // deliberadamente a janela de sobreposição entre as duas requisições concorrentes do teste,
    // tornando um eventual vazamento de escopo determinístico em vez de uma corrida rara de vencer.
    app.MapGet("/teste/tenant-atual", async (ITenantContext tenantContext) =>
    {
        await Task.Delay(100);
        return Results.Text(tenantContext.ContaId.ToString());
    });

    // Endpoints de diagnóstico do middleware de Idempotency-Key (Tarefa 4, spec B4) — POST /v1/nfce real
    // só chega na Fase 3; estes stand-ins expõem o mesmo pipeline (RequireIdempotencyKey/AcceptIdempotencyKey)
    // para os testes de integração de tests/NotaFiscalHub.IntegrationTests/Idempotency exercitarem o filter
    // ponta a ponta contra um host real. ContadorDoHandlerFake conta execuções REAIS do "handler" (nunca
    // incrementado em replay/409 — o filter intercepta antes).
    app.MapPost("/v1/nfce", async (HttpContext http, ContadorDoHandlerFake contador) =>
        {
            contador.Incrementar();
            await Task.Yield();
            var id = Guid.NewGuid();
            return Results.Created($"/v1/nfce/{id}", new { id, status = "autorizada" });
        })
        .RequireIdempotencyKey();

    // Variante que sempre devolve 202 (contingência) — usada pelo teste de replay do 202 (spec B4,
    // critério de aceite 5): mesmo pipeline de idempotência, resposta diferente.
    app.MapPost("/v1/nfce/contingencia", async (HttpContext http, ContadorDoHandlerFake contador) =>
        {
            contador.Incrementar();
            await Task.Yield();
            return Results.Accepted($"/v1/nfce/{Guid.NewGuid()}", new { status = "contingencia", dadosImpressao = "QR-CODE-FAKE" });
        })
        .RequireIdempotencyKey();

    app.MapPost("/v1/inutilizacoes", async (HttpContext http, ContadorDoHandlerFake contador) =>
        {
            contador.Incrementar();
            await Task.Yield();
            return Results.Ok(new { status = "inutilizada" });
        })
        .RequireIdempotencyKey();

    app.MapGet("/v1/nfce/{id}", (string id) => Results.Ok(new { id }));

    app.MapPost("/teste/idempotency/resetar-contador", (ContadorDoHandlerFake contador) =>
    {
        contador.Resetar();
        return Results.Ok();
    });
}

app.Run();

// Necessário para que o WebApplicationFactory<Program> dos testes de integração enxergue o entry point.
public partial class Program;
