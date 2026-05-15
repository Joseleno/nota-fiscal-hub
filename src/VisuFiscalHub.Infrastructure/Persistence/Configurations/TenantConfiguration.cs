using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.ValueObjects;

namespace VisuFiscalHub.Infrastructure.Persistence.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new TenantId(value));

        builder.Property(t => t.ClienteAppId)
            .HasColumnName("cliente_app_id")
            .HasConversion(id => id.Value, value => new ClienteAppId(value))
            .IsRequired();

        builder.HasOne<ClienteApp>()
            .WithMany()
            .HasForeignKey(t => t.ClienteAppId)
            .HasConstraintName("fk_tenants_cliente_app_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => t.ClienteAppId)
            .HasDatabaseName("ix_tenants_cliente_app_id");

        builder.Property(t => t.Cnpj)
            .HasColumnName("cnpj")
            .HasMaxLength(14)
            .IsRequired()
            .HasConversion(cnpj => cnpj.Valor, valor => Cnpj.FromStorage(valor));

        builder.HasIndex(t => new { t.Cnpj, t.ClienteAppId })
            .IsUnique()
            .HasDatabaseName("ix_tenants_cnpj_cliente_app_id");

        builder.Property(t => t.RazaoSocial)
            .HasColumnName("razao_social")
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(t => t.NomeFantasia)
            .HasColumnName("nome_fantasia")
            .HasMaxLength(300);

        builder.OwnsOne(t => t.Endereco, end =>
        {
            end.Property(e => e.Logradouro).HasColumnName("end_logradouro").HasMaxLength(200).IsRequired();
            end.Property(e => e.Numero).HasColumnName("end_numero").HasMaxLength(10).IsRequired();
            end.Property(e => e.Complemento).HasColumnName("end_complemento").HasMaxLength(100);
            end.Property(e => e.Bairro).HasColumnName("end_bairro").HasMaxLength(100).IsRequired();
            end.Property(e => e.Municipio).HasColumnName("end_municipio").HasMaxLength(100).IsRequired();
            // CodigoMunicipio é int (código IBGE 7 dígitos) — mapeado como integer
            end.Property(e => e.CodigoMunicipio).HasColumnName("end_codigo_municipio").IsRequired();
            end.Property(e => e.Uf).HasColumnName("end_uf").HasMaxLength(2).IsRequired();
            end.Property(e => e.Cep).HasColumnName("end_cep").HasMaxLength(8).IsRequired();
            end.Property(e => e.CodigoPais).HasColumnName("end_codigo_pais").HasMaxLength(4).IsRequired();
            end.Property(e => e.Telefone).HasColumnName("end_telefone").HasMaxLength(20);
        });

        builder.OwnsOne(t => t.ConfiguracaoFiscal, cfg =>
        {
            cfg.Property(c => c.Crt)
                .HasColumnName("crt")
                .HasConversion<int>()
                .IsRequired();
            cfg.Property(c => c.Serie)
                .HasColumnName("serie")
                .HasMaxLength(3)
                .IsRequired();
            cfg.Property(c => c.Ambiente)
                .HasColumnName("ambiente")
                .HasConversion<int>()
                .IsRequired();
            cfg.Property(c => c.UfCodigo)
                .HasColumnName("uf_codigo")
                .IsRequired();
        });

        builder.Property(t => t.Csc)
            .HasColumnName("csc")
            .HasColumnType("bytea");

        builder.Property(t => t.CIdToken)
            .HasColumnName("c_id_token")
            .HasMaxLength(6);

        builder.Property(t => t.CertificadoPfxCriptografado)
            .HasColumnName("certificado_pfx_criptografado")
            .HasColumnType("bytea");

        builder.Property(t => t.CertificadoSenhaCriptografada)
            .HasColumnName("certificado_senha_criptografada")
            .HasColumnType("bytea");

        builder.Property(t => t.CertificadoVencimento)
            .HasColumnName("certificado_vencimento");

        builder.Property(t => t.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();
    }
}
