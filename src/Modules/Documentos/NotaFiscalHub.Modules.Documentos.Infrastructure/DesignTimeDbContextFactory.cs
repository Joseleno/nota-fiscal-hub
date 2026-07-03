using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;

namespace NotaFiscalHub.Modules.Documentos.Infrastructure;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DocumentosDbContext>
{
    public DocumentosDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("NOTAFISCALHUB_CONNECTION_STRING")
            ?? "Host=localhost;Database=notafiscalhub;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<DocumentosDbContext>();
        optionsBuilder.UseNpgsql(connectionString, o => o.MigrationsHistoryTable("__ef_migrations_history", "documentos"));

        // Design-time (dotnet ef migrations) só monta o model — nunca executa query real —
        // então um escopo de sistema é suficiente para satisfazer o construtor de TenantDbContext.
        var tenantContext = new AmbientTenantContext();
        tenantContext.BeginSystemScope("design-time", "dotnet-ef");

        return new DocumentosDbContext(optionsBuilder.Options, tenantContext);
    }
}
