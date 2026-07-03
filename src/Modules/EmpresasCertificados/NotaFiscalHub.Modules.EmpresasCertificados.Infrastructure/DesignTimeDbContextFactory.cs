using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NotaFiscalHub.Modules.EmpresasCertificados.Infrastructure;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<EmpresasCertificadosDbContext>
{
    public EmpresasCertificadosDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("NOTAFISCALHUB_CONNECTION_STRING")
            ?? "Host=localhost;Database=notafiscalhub;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<EmpresasCertificadosDbContext>();
        optionsBuilder.UseNpgsql(connectionString, o => o.MigrationsHistoryTable("__ef_migrations_history", "empresas"));

        return new EmpresasCertificadosDbContext(optionsBuilder.Options);
    }
}
