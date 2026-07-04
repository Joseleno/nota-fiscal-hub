using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;

namespace NotaFiscalHub.BuildingBlocks.Idempotency;

/// <summary>
/// Linha mapeada por EF Core de <c>kernel.idempotency_registro</c> (spec B4 §Abordagem passo 2). Classe
/// mutável separada do record imutável <see cref="IdempotencyRecord"/> (que representa uma decisão/valor,
/// não uma entidade rastreada) — mesmo desenho de <c>OutboxMessage</c> (Tarefa 3).
///
/// Implementa <see cref="ITenantScopedEntity"/> mas fica na allowlist de entidades globais do teste de
/// arquitetura (mesma justificativa de <c>OutboxMessage</c>/<c>InboxMessage</c>): o job de expiração
/// (<see cref="IdempotencyExpirationJob"/>) precisa varrer <c>conta_id</c> distintas ACROSS tenants antes
/// de abrir <c>BeginTenantScope</c> por conta — um filtro global de tenant impediria essa varredura.
/// </summary>
public sealed class IdempotencyRecordEntity : ITenantScopedEntity
{
    public required Guid ContaId { get; set; }
    public required string Ambiente { get; set; }
    public required string Key { get; set; }
    public required string Rota { get; set; }
    public required string PayloadHashSha256 { get; set; }
    public required IdempotencyState Estado { get; set; }
    public int? RespostaStatus { get; set; }
    public string? RespostaCorpo { get; set; }
    public string? RespostaContentType { get; set; }
    public string? RespostaLocation { get; set; }
    public required DateTimeOffset CriadaEm { get; set; }
    public required DateTimeOffset ExpiraEm { get; set; }
}
