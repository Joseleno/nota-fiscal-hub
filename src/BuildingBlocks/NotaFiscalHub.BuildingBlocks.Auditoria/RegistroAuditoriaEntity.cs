namespace NotaFiscalHub.BuildingBlocks.Auditoria;

/// <summary>
/// Linha mapeada por EF Core de <c>auditoria.registro_auditoria</c> (spec B5 §Abordagem passo 3). Classe
/// mutável separada do record imutável <c>RegistroAuditoria</c> (contrato) — mesmo desenho de
/// <c>OutboxMessage</c>/<c>IdempotencyRecordEntity</c>.
///
/// DELIBERADAMENTE NÃO implementa <c>ITenantScopedEntity</c>: essa interface exige <c>ContaId</c> não
/// anulável (contrato fixo do kernel — <c>ITenantScopedEntity.ContaId</c> é <see cref="Guid"/>, não
/// <see cref="Nullable{Guid}"/>), mas <see cref="ContaId"/> AQUI é <see cref="Nullable{Guid}"/> por design —
/// registros de escopo de sistema (ações administrativas sem tenant, ex.: <c>ContaSuspensa</c> disparada
/// pelo próprio sistema) têm <c>ContaId = null</c> (spec B5 §Abordagem passo 6). Ver comentário de classe
/// de <see cref="AuditoriaDbContext"/> para o racional completo (mesma exceção documentada para
/// <c>OutboxMessage</c>/<c>InboxMessage</c>, Tarefa 3).
///
/// Sem setters de mutação pós-insert previstos no repositório (nenhum método de update/delete em
/// <c>ConsultaAuditoria</c>/<c>AuditoriaEventHandler</c>) — a imutabilidade em nível de aplicação é reforçada
/// pelo trigger de banco <c>auditoria.bloquear_mutacao()</c> (spec B5 §Abordagem passo 3, critério de
/// aceite 3).
/// </summary>
public sealed class RegistroAuditoriaEntity
{
    public required Guid Id { get; set; }
    public Guid? ContaId { get; set; }
    public required string TipoEvento { get; set; }
    public required string Acao { get; set; }
    public required string RecursoTipo { get; set; }
    public required string RecursoId { get; set; }
    public required string Ator { get; set; }
    public required DateTimeOffset OcorridoEm { get; set; }
    public required DateTimeOffset RegistradoEm { get; set; }
    public required string CorrelationId { get; set; }
    public required Guid MessageId { get; set; }
    public string? Dados { get; set; }
}
