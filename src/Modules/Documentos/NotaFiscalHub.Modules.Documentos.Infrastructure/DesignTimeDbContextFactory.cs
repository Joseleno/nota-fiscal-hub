using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NotaFiscalHub.Modules.Documentos.Infrastructure;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DocumentosDbContext>
{
    public DocumentosDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("NOTAFISCALHUB_CONNECTION_STRING")
            ?? "Host=localhost;Database=notafiscalhub;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<DocumentosDbContext>();
        optionsBuilder.UseNpgsql(connectionString, o => o.MigrationsHistoryTable("__ef_migrations_history", "documentos"));

        return new DocumentosDbContext(optionsBuilder.Options);
    }
}
