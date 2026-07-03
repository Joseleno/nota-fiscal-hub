using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;
using NotaFiscalHub.Worker;

var builder = Host.CreateApplicationBuilder(args);

// Ver comentário equivalente em NotaFiscalHub.Api/Program.cs: singleton é obrigatório para o cache de
// model do EF Core por DbContext funcionar corretamente (TenantModelCacheKeyFactory).
builder.Services.AddSingleton<AmbientTenantContext>();
builder.Services.AddSingleton<ITenantContext>(sp => sp.GetRequiredService<AmbientTenantContext>());
builder.Services.AddSingleton<ITenantScopeFactory>(sp => sp.GetRequiredService<AmbientTenantContext>());
builder.Services.AddSingleton<TenantJobExecutor>();

builder.Build().Run();
