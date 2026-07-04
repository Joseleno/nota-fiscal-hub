using NotaFiscalHub.BuildingBlocks.Messaging.Abstractions;

namespace NotaFiscalHub.BuildingBlocks.Auditoria;

// Eventos concretos das tarefas de Contas & Planos (Fase 1) ainda não existem no código no momento da
// Tarefa 5 (spec B5 §Dependências: "a fundação testa com eventos sintéticos se ainda não existirem, mesmo
// padrão do metering na Fase 1"). Os 6 tipos abaixo são as formas SINTÉTICAS/MÍNIMAS desses eventos —
// nomes finais e formato exato serão definidos pelas B-tarefas reais da Fase 1; até lá, estes tipos
// concretos são o que popula o catálogo (AuditoriaEventCatalog) e o que os testes de integração publicam
// via outbox. Quando os eventos reais da Fase 1 existirem, o catálogo troca a referência de tipo (mudança
// mecânica, sem alterar o desenho do catálogo em si).
//
// Cada evento carrega apenas identificadores (Guid) — nunca segredo/token/PII bruto — conforme
// EventPayloadRules (regra de payload da Tarefa 3, spec B3 critério 6): o "Ator" é montado pelo handler a
// partir de um prefixo curto (ex.: "nfh_live_ab12"), nunca da chave completa.

/// <summary>Conta criada — ação administrativa raiz do tenant.</summary>
public sealed record ContaCriada : EventoIntegracao
{
    public required Guid CriadaContaId { get; init; }
    public required string AtorPrefixo { get; init; }
}

/// <summary>Rotação de API key de uma conta.</summary>
public sealed record ApiKeyRotacionada : EventoIntegracao
{
    public required Guid ApiKeyId { get; init; }
    public required string AtorPrefixo { get; init; }
}

/// <summary>Revogação de API key de uma conta.</summary>
public sealed record ApiKeyRevogada : EventoIntegracao
{
    public required Guid ApiKeyId { get; init; }
    public required string AtorPrefixo { get; init; }
}

/// <summary>Alteração de configuração de webhook de uma conta.</summary>
public sealed record WebhookConfigAlterada : EventoIntegracao
{
    public required Guid WebhookConfigId { get; init; }
    public required string AtorPrefixo { get; init; }
}

/// <summary>Alteração de plano contratado de uma conta.</summary>
public sealed record PlanoAlterado : EventoIntegracao
{
    public required Guid PlanoId { get; init; }
    public required string AtorPrefixo { get; init; }
}

/// <summary>Suspensão de conta (ex.: inadimplência) — ação de sistema/backoffice.</summary>
public sealed record ContaSuspensa : EventoIntegracao
{
    public required string AtorPrefixo { get; init; }
}
