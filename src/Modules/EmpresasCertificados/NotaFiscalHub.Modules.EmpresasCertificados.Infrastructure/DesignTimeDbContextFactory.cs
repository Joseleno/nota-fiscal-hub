using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;

namespace NotaFiscalHub.Modules.EmpresasCertificados.Infrastructure;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<EmpresasCertificadosDbContext>
{
    public EmpresasCertificadosDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("NOTAFISCALHUB_CONNECTION_STRING")
            ?? "Host=localhost;Database=notafiscalhub;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<EmpresasCertificadosDbContext>();
        optionsBuilder.UseNpgsql(connectionString, o => o.MigrationsHistoryTable("__ef_migrations_history", "empresas"));

        // Design-time (dotnet ef migrations) só monta o model — nunca executa query real —
        // então um escopo de sistema é suficiente para satisfazer o construtor de TenantDbContext.
        var tenantContext = new AmbientTenantContext();
        tenantContext.BeginSystemScope("design-time", "dotnet-ef");

        return new EmpresasCertificadosDbContext(optionsBuilder.Options, tenantContext);
    }
}
