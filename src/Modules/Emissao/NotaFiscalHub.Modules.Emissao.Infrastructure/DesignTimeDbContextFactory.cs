using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;

namespace NotaFiscalHub.Modules.Emissao.Infrastructure;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<EmissaoDbContext>
{
    public EmissaoDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("NOTAFISCALHUB_CONNECTION_STRING")
            ?? "Host=localhost;Database=notafiscalhub;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<EmissaoDbContext>();
        optionsBuilder.UseNpgsql(connectionString, o => o.MigrationsHistoryTable("__ef_migrations_history", "emissao"));

        // Design-time (dotnet ef migrations) só monta o model — nunca executa query real —
        // então um escopo de sistema é suficiente para satisfazer o construtor de TenantDbContext.
        var tenantContext = new AmbientTenantContext();
        tenantContext.BeginSystemScope("design-time", "dotnet-ef");

        return new EmissaoDbContext(optionsBuilder.Options, tenantContext);
    }
}
