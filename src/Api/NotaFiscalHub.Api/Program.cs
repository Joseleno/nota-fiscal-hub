using NotaFiscalHub.Api.Authentication;
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

var app = builder.Build();

app.MapGet("/alive", () => Results.Ok());
app.MapGet("/health", () => Results.Ok());

app.UseMiddleware<TenantResolutionMiddleware>();

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
}

app.Run();

// Necessário para que o WebApplicationFactory<Program> dos testes de integração enxergue o entry point.
public partial class Program;
