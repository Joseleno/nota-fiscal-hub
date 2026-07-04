using Microsoft.EntityFrameworkCore;

namespace NotaFiscalHub.BuildingBlocks.Idempotency;

/// <summary>
/// Aplica a tabela <c>idempotency_registro</c> no schema <c>kernel</c> (spec B4 §Abordagem passo 2 — PK
/// composta <c>(conta_id, ambiente, rota, key)</c>, índice em <c>expira_em</c> para o job de expiração).
/// Schema próprio do kernel, dono = kernel — mesma exceção documentada para o schema <c>auditoria</c> da
/// Tarefa 5 (design §2.5): não é schema de nenhum módulo de negócio.
/// </summary>
public static class IdempotencyModelBuilderExtensions
{
    public const string Schema = "kernel";

    public static void AplicarIdempotency(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdempotencyRecordEntity>(b =>
        {
            b.ToTable("idempotency_registro", Schema);
            b.HasKey(x => new { x.ContaId, x.Ambiente, x.Rota, x.Key });

            b.Property(x => x.ContaId).HasColumnName("conta_id");
            b.Property(x => x.Ambiente).HasColumnName("ambiente").IsRequired();
            b.Property(x => x.Rota).HasColumnName("rota").IsRequired();
            b.Property(x => x.Key).HasColumnName("key").IsRequired();
            b.Property(x => x.PayloadHashSha256).HasColumnName("payload_hash_sha256").IsRequired();
            b.Property(x => x.Estado).HasColumnName("estado").HasConversion<string>().IsRequired();
            b.Property(x => x.RespostaStatus).HasColumnName("resposta_status");
            b.Property(x => x.RespostaCorpo).HasColumnName("resposta_corpo");
            b.Property(x => x.RespostaContentType).HasColumnName("resposta_content_type");
            b.Property(x => x.RespostaLocation).HasColumnName("resposta_location");
            b.Property(x => x.CriadaEm).HasColumnName("criada_em").IsRequired();
            b.Property(x => x.ExpiraEm).HasColumnName("expira_em").IsRequired();

            b.HasIndex(x => x.ExpiraEm);
        });
    }
}
