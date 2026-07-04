using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NotaFiscalHub.BuildingBlocks.Auditoria;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AuditoriaDbContext>
{
    public AuditoriaDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("NOTAFISCALHUB_CONNECTION_STRING")
            ?? "Host=localhost;Database=notafiscalhub;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<AuditoriaDbContext>();
        optionsBuilder.UseNpgsql(connectionString, o => o.MigrationsHistoryTable("__ef_migrations_history", "auditoria"));

        // Diferente dos DbContexts derivados de TenantDbContext, AuditoriaDbContext não recebe
        // ITenantContext — construção em tempo de design não precisa de nenhum escopo de tenant.
        return new AuditoriaDbContext(optionsBuilder.Options);
    }
}
