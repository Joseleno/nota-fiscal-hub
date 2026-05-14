using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Infrastructure.Persistence.Configurations;

namespace VisuFiscalHub.Infrastructure.Persistence;

public sealed class ApplicationDbContext : DbContext
{
    private readonly ILoggerFactory _loggerFactory;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ILoggerFactory loggerFactory)
        : base(options)
    {
        _loggerFactory = loggerFactory;
    }

    public DbSet<ClienteApp> ClienteApps => Set<ClienteApp>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<DocumentoFiscal> DocumentosFiscais => Set<DocumentoFiscal>();
    public DbSet<DeliveryAttempt> DeliveryAttempts => Set<DeliveryAttempt>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ClienteAppConfiguration());
        modelBuilder.ApplyConfiguration(new TenantConfiguration());
        modelBuilder.ApplyConfiguration(new DocumentoFiscalConfiguration(
            _loggerFactory.CreateLogger<DocumentoFiscalConfiguration>()));
        modelBuilder.ApplyConfiguration(new DeliveryAttemptConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
    }
}
