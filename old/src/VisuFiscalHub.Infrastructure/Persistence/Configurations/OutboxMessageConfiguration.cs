using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace VisuFiscalHub.Infrastructure.Persistence.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id");

        builder.Property(o => o.EventType)
            .HasColumnName("event_type")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(o => o.Payload)
            .HasColumnName("payload")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(o => o.OccurredAt)
            .HasColumnName("occurred_at")
            .IsRequired();

        builder.Property(o => o.ProcessedAt)
            .HasColumnName("processed_at");

        builder.Property(o => o.CorrelationId)
            .HasColumnName("correlation_id")
            .HasMaxLength(128);

        // Índice (processed_at, occurred_at): suporta WHERE processed_at IS NULL ORDER BY occurred_at
        // do OutboxRelayJob — NULLs ficam no início no PostgreSQL (NULLS FIRST padrão)
        builder.HasIndex(o => new { o.ProcessedAt, o.OccurredAt })
            .HasDatabaseName("ix_outbox_messages_processed_at_occurred_at");
    }
}
