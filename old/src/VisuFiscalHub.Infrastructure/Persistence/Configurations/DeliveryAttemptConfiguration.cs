using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Infrastructure.Persistence.Configurations;

public sealed class DeliveryAttemptConfiguration : IEntityTypeConfiguration<DeliveryAttempt>
{
    public void Configure(EntityTypeBuilder<DeliveryAttempt> builder)
    {
        builder.ToTable("delivery_attempts");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new DeliveryAttemptId(value));

        builder.Property(d => d.DocumentoFiscalId)
            .HasColumnName("documento_fiscal_id")
            .HasConversion(id => id.Value, value => new DocumentoFiscalId(value))
            .IsRequired();

        builder.HasOne<DocumentoFiscal>()
            .WithMany()
            .HasForeignKey(d => d.DocumentoFiscalId)
            .HasConstraintName("fk_delivery_attempts_documento_fiscal_id")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(d => d.TipoTentativa)
            .HasColumnName("tipo_tentativa")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(d => d.AttemptedAt)
            .HasColumnName("attempted_at")
            .IsRequired();

        builder.Property(d => d.Success)
            .HasColumnName("success")
            .IsRequired();

        builder.Property(d => d.ResponseCode)
            .HasColumnName("response_code")
            .HasMaxLength(10);

        builder.Property(d => d.ResponseMessage)
            .HasColumnName("response_message")
            .HasMaxLength(500);

        builder.Property(d => d.ElapsedMs)
            .HasColumnName("elapsed_ms")
            .IsRequired();

        builder.HasIndex(d => d.DocumentoFiscalId)
            .HasDatabaseName("ix_delivery_attempts_documento_fiscal_id");
    }
}
