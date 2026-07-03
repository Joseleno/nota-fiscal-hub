using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NotaFiscalHub.Modules.ContasPlanos.Infrastructure;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ContasPlanosDbContext>
{
    public ContasPlanosDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("NOTAFISCALHUB_CONNECTION_STRING")
            ?? "Host=localhost;Database=notafiscalhub;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<ContasPlanosDbContext>();
        optionsBuilder.UseNpgsql(connectionString, o => o.MigrationsHistoryTable("__ef_migrations_history", "contas"));

        return new ContasPlanosDbContext(optionsBuilder.Options);
    }
}
