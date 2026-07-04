namespace NotaFiscalHub.BuildingBlocks.Auditoria.Contracts;

/// <summary>
/// Linha imutável da trilha de auditoria append-only (spec B5), materializada a partir de um evento de
/// integração consumido via inbox. Representa uma DECISÃO/LEITURA — não uma entidade rastreada por EF
/// Core (essa é <c>RegistroAuditoriaEntity</c>, no projeto de implementação) — mesmo desenho de
/// <c>IdempotencyRecord</c>/<c>IdempotencyRecordEntity</c> (Tarefa 4) e <c>OutboxMessage</c> (Tarefa 3).
/// </summary>
/// <param name="Id">Identidade própria do registro de auditoria (não é o <see cref="MessageId"/>).</param>
/// <param name="ContaId">
/// Tenant dono da ação auditada. <see langword="null"/> apenas para registros de ESCOPO DE SISTEMA
/// (ações administrativas/backoffice sem tenant) — gravados pelo handler sob <c>BeginSystemScope</c>
/// (spec B5 §Abordagem passo 6). Nunca retornável por <see cref="IConsultaAuditoria"/>, que exige
/// <see cref="FiltroAuditoria.ContaId"/> não anulável.
/// </param>
/// <param name="TipoEvento">Nome versionado do evento de integração de origem (ex.: "ApiKeyRevogada.v1").</param>
/// <param name="Acao">Verbo do catálogo (ex.: "apikey.revogada").</param>
/// <param name="RecursoTipo">Tipo do recurso afetado (ex.: "ApiKey", "Conta", "WebhookConfig").</param>
/// <param name="RecursoId">Id opaco do recurso (GUID em string).</param>
/// <param name="Ator">
/// Quem: prefixo do canal + identificador parcial (ex.: "apikey:nfh_live_ab12…", "portal:usuarioId",
/// "sistema:worker") — nunca o segredo/token completo.
/// </param>
/// <param name="OcorridoEm">Timestamp do evento de origem (para leitura humana — ver Riscos da spec B5).</param>
/// <param name="RegistradoEm">Timestamp da materialização, UTC, gerado no insert (para reconciliação técnica).</param>
/// <param name="CorrelationId">Propagado request → outbox → inbox.</param>
/// <param name="MessageId">Id da mensagem de integração de origem — chave de dedupe.</param>
/// <param name="DadosJson">jsonb: só identificadores/valores não sensíveis, pós-sanitização (<c>PiiSanitizer</c>).</param>
public sealed record RegistroAuditoria(
    Guid Id,
    Guid? ContaId,
    string TipoEvento,
    string Acao,
    string RecursoTipo,
    string RecursoId,
    string Ator,
    DateTimeOffset OcorridoEm,
    DateTimeOffset RegistradoEm,
    string CorrelationId,
    Guid MessageId,
    string? DadosJson);

/// <summary>
/// Forma pré-materialização de um registro de auditoria — o que <see cref="IAuditoriaEventCatalog.TryMap"/>
/// consegue derivar PURAMENTE do evento de integração recebido, sem acesso a infraestrutura. Campos que só
/// existem no momento da persistência (<see cref="RegistroAuditoria.Id"/>, <see cref="RegistroAuditoria.RegistradoEm"/>,
/// <see cref="RegistroAuditoria.CorrelationId"/>, <see cref="RegistroAuditoria.MessageId"/>) são preenchidos
/// pelo handler (<c>AuditoriaEventHandler</c>), não pelo catálogo.
/// </summary>
public sealed record RegistroAuditoriaNovo(
    Guid? ContaId,
    string TipoEvento,
    string Acao,
    string RecursoTipo,
    string RecursoId,
    string Ator,
    DateTimeOffset OcorridoEm,
    string? DadosJson);
