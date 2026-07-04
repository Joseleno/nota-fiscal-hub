namespace NotaFiscalHub.BuildingBlocks.Auditoria.Contracts;

/// <summary>
/// Filtro de consulta da trilha de auditoria (spec B5 §Abordagem passo 6). <see cref="ContaId"/> é
/// OBRIGATÓRIO (não anulável, ao contrário de <see cref="RegistroAuditoria.ContaId"/>) — não existe
/// consulta sem tenant nesta fundação; leitura de registros de escopo de sistema fica fora do MVP,
/// reservada ao endpoint de Backoffice da Fase 6 (que usará o bypass documentado, não este contrato).
/// </summary>
public sealed record FiltroAuditoria(
    Guid ContaId,
    DateTimeOffset? De = null,
    DateTimeOffset? Ate = null,
    string? Acao = null,
    string? RecursoTipo = null,
    int Pagina = 1,
    int TamanhoPagina = 50);
