using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NotaFiscalHub.Modules.Emissao.Infrastructure;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<EmissaoDbContext>
{
    public EmissaoDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("NOTAFISCALHUB_CONNECTION_STRING")
            ?? "Host=localhost;Database=notafiscalhub;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<EmissaoDbContext>();
        optionsBuilder.UseNpgsql(connectionString, o => o.MigrationsHistoryTable("__ef_migrations_history", "emissao"));

        return new EmissaoDbContext(optionsBuilder.Options);
    }
}
