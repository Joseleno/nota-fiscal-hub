using Microsoft.EntityFrameworkCore;

namespace NotaFiscalHub.BuildingBlocks.Messaging;

/// <summary>
/// Aplica o par de tabelas <c>outbox</c>/<c>inbox</c> no schema de um módulo. Chamado a partir de
/// <c>OnModelCreating</c> de cada <c>&lt;Modulo&gt;DbContext</c> que publica e/ou consome eventos de
/// integração (spec B3 passo 2).
/// </summary>
public static class OutboxInboxModelBuilderExtensions
{
    public static void AplicarOutboxInbox(this ModelBuilder modelBuilder, string schema)
    {
        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("outbox", schema);
            b.HasKey(x => x.Id); // Id = MessageId
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.TipoEvento).HasColumnName("tipo_evento").IsRequired();
            b.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
            b.Property(x => x.ContaId).HasColumnName("conta_id");
            b.Property(x => x.CorrelationId).HasColumnName("correlation_id").IsRequired();
            b.Property(x => x.OcorridoEm).HasColumnName("ocorrido_em").IsRequired();
            b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().IsRequired();
            b.Property(x => x.Tentativas).HasColumnName("tentativas").IsRequired();
            b.Property(x => x.ProximaTentativaEm).HasColumnName("proxima_tentativa_em").IsRequired();
            b.Property(x => x.ProcessadaEm).HasColumnName("processada_em");
            b.Property(x => x.ErroUltimo).HasColumnName("erro_ultimo");

            b.HasIndex(x => new { x.Status, x.ProximaTentativaEm })
                .HasFilter("status = 'Pendente'");
        });

        modelBuilder.Entity<InboxMessage>(b =>
        {
            b.ToTable("inbox", schema);
            b.HasKey(x => new { x.MessageId, x.Handler }); // dedupe por handler
            b.Property(x => x.MessageId).HasColumnName("message_id");
            b.Property(x => x.Handler).HasColumnName("handler").IsRequired();
            b.Property(x => x.TipoEvento).HasColumnName("tipo_evento").IsRequired();
            b.Property(x => x.ProcessadaEm).HasColumnName("processada_em").IsRequired();
        });
    }
}
