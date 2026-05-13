using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VisuFiscalHub.Domain.Interfaces;
using VisuFiscalHub.Infrastructure.Persistence;
using VisuFiscalHub.Infrastructure.Persistence.Repositories;

namespace VisuFiscalHub.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Singleton explícito — sem dependências scoped; evita scope leak silencioso se dependências forem adicionadas
        services.AddSingleton<DomainEventsInterceptor>();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Connection string 'DefaultConnection' não configurada.");

        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsAssembly(
                    typeof(ApplicationDbContext).Assembly.FullName));

            options.AddInterceptors(sp.GetRequiredService<DomainEventsInterceptor>());
        });

        services.AddSingleton(TimeProvider.System);

        services.AddScoped<IClienteAppRepository, ClienteAppRepository>();
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IDocumentoFiscalRepository, DocumentoFiscalRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }
}
