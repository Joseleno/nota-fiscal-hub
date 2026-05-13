using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Infrastructure.Persistence.Configurations;

public sealed class ClienteAppConfiguration : IEntityTypeConfiguration<ClienteApp>
{
    public void Configure(EntityTypeBuilder<ClienteApp> builder)
    {
        builder.ToTable("cliente_apps");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new ClienteAppId(value));

        builder.Property(c => c.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(c => c.ClientId)
            .HasColumnName("client_id")
            .HasMaxLength(100)
            .IsRequired();

        builder.HasIndex(c => c.ClientId)
            .IsUnique()
            .HasDatabaseName("ix_cliente_apps_client_id");

        builder.Property(c => c.ClientSecretHash)
            .HasColumnName("client_secret_hash")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(c => c.WebhookUrl)
            .HasColumnName("webhook_url")
            .HasMaxLength(500);

        builder.Property(c => c.WebhookSecretCriptografado)
            .HasColumnName("webhook_secret_criptografado")
            .HasColumnType("bytea");

        builder.Property(c => c.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(c => c.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();
    }
}
