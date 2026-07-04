using Microsoft.EntityFrameworkCore;

namespace NotaFiscalHub.BuildingBlocks.Auditoria;

/// <summary>
/// Aplica a tabela <c>registro_auditoria</c> no schema <c>auditoria</c> (spec B5 §Abordagem passo 3): PK
/// <c>id</c>, índice composto <c>(conta_id, registrado_em DESC)</c> (consulta paginada por tenant ordenada
/// por materialização), índice único em <c>message_id</c> (segunda barreira de dedupe, além do inbox —
/// <c>ON CONFLICT (message_id) DO NOTHING</c> no insert), coluna <c>dados</c> tipada <c>jsonb</c>.
/// </summary>
public static class AuditoriaModelBuilderExtensions
{
    public const string Schema = "auditoria";

    public static void AplicarAuditoria(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RegistroAuditoriaEntity>(b =>
        {
            b.ToTable("registro_auditoria", Schema);
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

            b.Property(x => x.ContaId).HasColumnName("conta_id");
            b.Property(x => x.TipoEvento).HasColumnName("tipo_evento").IsRequired();
            b.Property(x => x.Acao).HasColumnName("acao").IsRequired();
            b.Property(x => x.RecursoTipo).HasColumnName("recurso_tipo").IsRequired();
            b.Property(x => x.RecursoId).HasColumnName("recurso_id").IsRequired();
            b.Property(x => x.Ator).HasColumnName("ator").IsRequired();
            b.Property(x => x.OcorridoEm).HasColumnName("ocorrido_em").IsRequired();
            b.Property(x => x.RegistradoEm).HasColumnName("registrado_em").IsRequired();
            b.Property(x => x.CorrelationId).HasColumnName("correlation_id").IsRequired();
            b.Property(x => x.MessageId).HasColumnName("message_id").IsRequired();
            b.Property(x => x.Dados).HasColumnName("dados").HasColumnType("jsonb");

            b.HasIndex(x => new { x.ContaId, x.RegistradoEm })
                .HasDatabaseName("ix_registro_auditoria_conta_id_registrado_em")
                .IsDescending(false, true); // conta_id ASC, registrado_em DESC (spec B5 §Abordagem passo 3).

            b.HasIndex(x => x.MessageId)
                .IsUnique()
                .HasDatabaseName("ux_registro_auditoria_message_id");
        });
    }
}
