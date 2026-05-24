using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Logging;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.ValueObjects;

namespace VisuFiscalHub.Infrastructure.Persistence.Configurations;

public sealed class DocumentoFiscalConfiguration : IEntityTypeConfiguration<DocumentoFiscal>
{
    private readonly ILogger<DocumentoFiscalConfiguration> _logger;

    public DocumentoFiscalConfiguration(ILogger<DocumentoFiscalConfiguration> logger)
    {
        _logger = logger;
    }

    // ValueConverter não suporta lambdas com statement body, por isso o método auxiliar.
    // Dado corrompido: loga e usa bypass em vez de derrubar a query inteira.
    private ChaveAcesso ChaveAcessoFromStorage(string valor)
    {
        var result = ChaveAcesso.From(valor);
        if (result.IsFailure)
        {
            _logger.LogError(
                "Chave de acesso corrompida no banco de dados: '{Valor}'. Erro: {Codigo}. Documento será carregado com chave inválida.",
                valor, result.Error.Code);
            return ChaveAcesso.FromStorage(valor);
        }
        return result.Value;
    }

    public void Configure(EntityTypeBuilder<DocumentoFiscal> builder)
    {
        builder.ToTable("documentos_fiscais");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new DocumentoFiscalId(value));

        builder.Property(d => d.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(d => d.TenantId)
            .HasConstraintName("fk_documentos_fiscais_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);

        // Armazenado para evitar query adicional ao publicar eventos de domínio
        builder.Property(d => d.ClienteAppId)
            .HasColumnName("cliente_app_id")
            .HasConversion(id => id.Value, value => new ClienteAppId(value))
            .IsRequired();

        builder.Property(d => d.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(100)
            .IsRequired();

        builder.HasIndex(d => new { d.TenantId, d.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ix_documentos_fiscais_tenant_idempotency");

        builder.Property(d => d.Tipo)
            .HasColumnName("tipo")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(d => d.ChaveAcesso)
            .HasColumnName("chave_acesso")
            .HasMaxLength(44)
            .IsRequired()
            .HasConversion(new ValueConverter<ChaveAcesso, string>(
                ca => ca.Valor,
                valor => ChaveAcessoFromStorage(valor)));

        builder.HasIndex(d => d.ChaveAcesso)
            .HasDatabaseName("ix_documentos_fiscais_chave_acesso");

        builder.Property(d => d.Numero)
            .HasColumnName("numero")
            .IsRequired();

        builder.Property(d => d.Serie)
            .HasColumnName("serie")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(d => d.IndPresenca)
            .HasColumnName("ind_presenca")
            .IsRequired();

        builder.Property(d => d.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .IsRequired();

        builder.HasIndex(d => new { d.Status, d.CreatedAt })
            .HasDatabaseName("ix_documentos_fiscais_status_created_at");

        builder.Property(d => d.XmlAssinado)
            .HasColumnName("xml_assinado")
            .HasColumnType("text");

        builder.Property(d => d.Protocolo)
            .HasColumnName("protocolo")
            .HasMaxLength(50);

        builder.Property(d => d.QrCode)
            .HasColumnName("qr_code")
            .HasMaxLength(1000)
            .HasConversion(
                qr => qr == null ? null : qr.UrlCompleta,
                s => s == null ? null : QrCode.FromStorage(s));

        builder.Property(d => d.CpfConsumidor)
            .HasColumnName("cpf_consumidor")
            .HasMaxLength(11);

        builder.Property(d => d.NomeConsumidor)
            .HasColumnName("nome_consumidor")
            .HasMaxLength(60);

        builder.Property(d => d.MotivoRejeicao)
            .HasColumnName("motivo_rejeicao")
            .HasMaxLength(500);

        builder.Property(d => d.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(d => d.AuthorizedAt)
            .HasColumnName("authorized_at");

        // HasField obrigatório — a propriedade pública expõe IReadOnlyList<T>, incompatível com EF Core.
        // Declara o backing field antes do OwnsMany para que o EF Core use _items para leitura/escrita.
        builder.Navigation(d => d.Items).HasField("_items");
        builder.OwnsMany(d => d.Items, items =>
        {
            items.ToTable("itens_documento");

            // EF Core infere automaticamente o FK shadow property; apenas remapeamos o nome da coluna.
            items.WithOwner().HasForeignKey("DocumentoFiscalId");
            items.Property<DocumentoFiscalId>("DocumentoFiscalId")
                .HasColumnName("documento_fiscal_id")
                .HasConversion(id => id.Value, value => new DocumentoFiscalId(value));

            items.Property(i => i.Numero)
                .HasColumnName("numero")
                .IsRequired();

            // ValorTotal é derivado de Produto.ValorLiquido — ignorar; EF Core não pode persistir expressão sem setter.
            items.Ignore(i => i.ValorTotal);

            items.OwnsOne(i => i.Produto, prod =>
            {
                prod.Property(p => p.CodigoProduto).HasColumnName("produto_codigo").HasMaxLength(60).IsRequired();
                prod.Property(p => p.Descricao).HasColumnName("produto_descricao").HasMaxLength(120).IsRequired();
                prod.Property(p => p.Ncm).HasColumnName("produto_ncm").HasMaxLength(8).IsRequired();
                prod.Property(p => p.Cest).HasColumnName("produto_cest").HasMaxLength(7);
                prod.Property(p => p.CfopSaida).HasColumnName("produto_cfop").HasMaxLength(4).IsRequired();
                prod.Property(p => p.UnidadeComercial).HasColumnName("produto_unidade").HasMaxLength(6).IsRequired();
                prod.Property(p => p.Quantidade).HasColumnName("produto_quantidade").HasPrecision(15, 4).IsRequired();
                prod.Property(p => p.ValorUnitario).HasColumnName("produto_valor_unitario").HasPrecision(21, 10).IsRequired();
                prod.Property(p => p.ValorDesconto).HasColumnName("produto_valor_desconto").HasPrecision(15, 2);
                prod.Property(p => p.OrigemMercadoria).HasColumnName("produto_origem").HasConversion<int>().IsRequired();
                // Propriedades computadas — ignorar: EF Core não pode persistir expressão
                prod.Ignore(p => p.ValorBruto);
                prod.Ignore(p => p.ValorLiquido);
            });

            items.OwnsOne(i => i.Tributo, trib =>
            {
                trib.Property(t => t.TipoIcms).HasColumnName("trib_tipo_icms").HasConversion<int>().IsRequired();
                // CsosnOuCst é int (CSOSN/CST armazenados como inteiro)
                trib.Property(t => t.CsosnOuCst).HasColumnName("trib_csosn_ou_cst").IsRequired();
                trib.Property(t => t.AliquotaIcms).HasColumnName("trib_aliquota_icms").HasPrecision(7, 4);
                trib.Property(t => t.BaseCalculoIcms).HasColumnName("trib_base_calculo_icms").HasPrecision(15, 2);
                trib.Property(t => t.ValorIcms).HasColumnName("trib_valor_icms").HasPrecision(15, 2);
                trib.Property(t => t.CstPis).HasColumnName("trib_cst_pis").HasConversion<int>().IsRequired();
                trib.Property(t => t.BaseCalculoPis).HasColumnName("trib_base_calculo_pis").HasPrecision(15, 2);
                trib.Property(t => t.AliquotaPis).HasColumnName("trib_aliquota_pis").HasPrecision(7, 4);
                trib.Property(t => t.ValorPis).HasColumnName("trib_valor_pis").HasPrecision(15, 2);
                trib.Property(t => t.CstCofins).HasColumnName("trib_cst_cofins").HasConversion<int>().IsRequired();
                trib.Property(t => t.BaseCalculoCofins).HasColumnName("trib_base_calculo_cofins").HasPrecision(15, 2);
                trib.Property(t => t.AliquotaCofins).HasColumnName("trib_aliquota_cofins").HasPrecision(7, 4);
                trib.Property(t => t.ValorCofins).HasColumnName("trib_valor_cofins").HasPrecision(15, 2);
            });
        });

        builder.Navigation(d => d.Pagamentos).HasField("_pagamentos");
        builder.OwnsMany(d => d.Pagamentos, pags =>
        {
            pags.ToTable("pagamentos_documento");

            pags.WithOwner().HasForeignKey("DocumentoFiscalId");
            pags.Property<DocumentoFiscalId>("DocumentoFiscalId")
                .HasColumnName("documento_fiscal_id")
                .HasConversion(id => id.Value, value => new DocumentoFiscalId(value));

            pags.Property(p => p.TipoPagamento)
                .HasColumnName("tipo_pagamento")
                .HasConversion<int>()
                .IsRequired();

            pags.Property(p => p.Valor)
                .HasColumnName("valor")
                .HasPrecision(15, 2)
                .IsRequired();
        });
    }
}
