using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace VisuFiscalHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cliente_apps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    client_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    client_secret_hash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    webhook_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    webhook_secret_criptografado = table.Column<byte[]>(type: "bytea", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cliente_apps", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_app_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cnpj = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    razao_social = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    nome_fantasia = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    crt = table.Column<int>(type: "integer", nullable: false),
                    serie = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ambiente = table.Column<int>(type: "integer", nullable: false),
                    uf_codigo = table.Column<int>(type: "integer", nullable: false),
                    end_logradouro = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    end_numero = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    end_complemento = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    end_bairro = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    end_municipio = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    end_codigo_municipio = table.Column<int>(type: "integer", nullable: false),
                    end_uf = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    end_cep = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    end_codigo_pais = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    end_telefone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    csc = table.Column<byte[]>(type: "bytea", nullable: true),
                    c_id_token = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: true),
                    certificado_pfx_criptografado = table.Column<byte[]>(type: "bytea", nullable: true),
                    certificado_senha_criptografada = table.Column<byte[]>(type: "bytea", nullable: true),
                    certificado_vencimento = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenants", x => x.id);
                    table.ForeignKey(
                        name: "fk_tenants_cliente_app_id",
                        column: x => x.cliente_app_id,
                        principalTable: "cliente_apps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "documentos_fiscais",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_app_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    chave_acesso = table.Column<string>(type: "character varying(44)", maxLength: 44, nullable: false),
                    numero = table.Column<long>(type: "bigint", nullable: false),
                    serie = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    xml_assinado = table.Column<string>(type: "text", nullable: true),
                    protocolo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    qr_code = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    motivo_rejeicao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    authorized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documentos_fiscais", x => x.id);
                    table.ForeignKey(
                        name: "fk_documentos_fiscais_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "delivery_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    documento_fiscal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_tentativa = table.Column<int>(type: "integer", nullable: false),
                    attempted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    success = table.Column<bool>(type: "boolean", nullable: false),
                    response_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    response_message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    elapsed_ms = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_attempts", x => x.id);
                    table.ForeignKey(
                        name: "fk_delivery_attempts_documento_fiscal_id",
                        column: x => x.documento_fiscal_id,
                        principalTable: "documentos_fiscais",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "itens_documento",
                columns: table => new
                {
                    documento_fiscal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    produto_codigo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    produto_descricao = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    produto_ncm = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    produto_cest = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    produto_cfop = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    produto_unidade = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    produto_quantidade = table.Column<decimal>(type: "numeric(15,4)", precision: 15, scale: 4, nullable: false),
                    produto_valor_unitario = table.Column<decimal>(type: "numeric(21,10)", precision: 21, scale: 10, nullable: false),
                    produto_valor_desconto = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    produto_origem = table.Column<int>(type: "integer", nullable: false),
                    trib_tipo_icms = table.Column<int>(type: "integer", nullable: false),
                    trib_csosn_ou_cst = table.Column<int>(type: "integer", nullable: false),
                    trib_aliquota_icms = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    trib_base_calculo_icms = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    trib_valor_icms = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    trib_cst_pis = table.Column<int>(type: "integer", nullable: false),
                    trib_base_calculo_pis = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    trib_aliquota_pis = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    trib_valor_pis = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    trib_cst_cofins = table.Column<int>(type: "integer", nullable: false),
                    trib_base_calculo_cofins = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    trib_aliquota_cofins = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    trib_valor_cofins = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_itens_documento", x => new { x.documento_fiscal_id, x.Id });
                    table.ForeignKey(
                        name: "FK_itens_documento_documentos_fiscais_documento_fiscal_id",
                        column: x => x.documento_fiscal_id,
                        principalTable: "documentos_fiscais",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pagamentos_documento",
                columns: table => new
                {
                    documento_fiscal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tipo_pagamento = table.Column<int>(type: "integer", nullable: false),
                    valor = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pagamentos_documento", x => new { x.documento_fiscal_id, x.Id });
                    table.ForeignKey(
                        name: "FK_pagamentos_documento_documentos_fiscais_documento_fiscal_id",
                        column: x => x.documento_fiscal_id,
                        principalTable: "documentos_fiscais",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cliente_apps_client_id",
                table: "cliente_apps",
                column: "client_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_delivery_attempts_documento_fiscal_id",
                table: "delivery_attempts",
                column: "documento_fiscal_id");

            migrationBuilder.CreateIndex(
                name: "ix_documentos_fiscais_chave_acesso",
                table: "documentos_fiscais",
                column: "chave_acesso");

            migrationBuilder.CreateIndex(
                name: "ix_documentos_fiscais_status_created_at",
                table: "documentos_fiscais",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_documentos_fiscais_tenant_idempotency",
                table: "documentos_fiscais",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at_occurred_at",
                table: "outbox_messages",
                columns: new[] { "processed_at", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tenants_cliente_app_id",
                table: "tenants",
                column: "cliente_app_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenants_cnpj_cliente_app_id",
                table: "tenants",
                columns: new[] { "cnpj", "cliente_app_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery_attempts");

            migrationBuilder.DropTable(
                name: "itens_documento");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "pagamentos_documento");

            migrationBuilder.DropTable(
                name: "documentos_fiscais");

            migrationBuilder.DropTable(
                name: "tenants");

            migrationBuilder.DropTable(
                name: "cliente_apps");
        }
    }
}
