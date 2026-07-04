using Microsoft.Extensions.DependencyInjection;
using NotaFiscalHub.BuildingBlocks.Auditoria.Contracts;
using NotaFiscalHub.BuildingBlocks.Messaging;

namespace NotaFiscalHub.BuildingBlocks.Auditoria;

/// <summary>Extensões de DI consumidas pelo host (Worker/API) para ligar a fundação de auditoria.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra <see cref="IConsultaAuditoria"/>, <see cref="IAuditoriaEventCatalog"/> e inscreve
    /// <see cref="AuditoriaEventHandler"/> como consumidor inbox de cada um dos 6 eventos sintéticos desta
    /// fundação (spec B5 §Escopo: "registro no catálogo dos eventos existentes desde a Fase 0/1"). NÃO
    /// registra <see cref="AuditoriaDbContext"/> — o provider é responsabilidade do host, mesmo desenho de
    /// <c>AddOutboxInbox&lt;TDbContext&gt;</c>/<c>AddIdempotency</c>.
    ///
    /// <see cref="AuditoriaEventHandler"/> é inscrito UMA VEZ POR TIPO CONCRETO (não uma única vez contra
    /// <c>EventoIntegracao</c>): o <c>OutboxTypeRegistry&lt;TDbContext&gt;</c> resolve handlers por tipo CLR
    /// concreto da mensagem — ver comentário de classe de <see cref="AuditoriaEventHandler"/>. Fases 1+ que
    /// adicionarem novos eventos auditáveis ao catálogo devem também chamar
    /// <c>AddInboxHandler&lt;AuditoriaDbContext, TNovoEvento, AuditoriaEventHandler&gt;()</c> no ponto de
    /// composição do host — este método cobre só os 6 eventos conhecidos nesta fundação.
    /// </summary>
    public static IServiceCollection AddAuditoria(this IServiceCollection services)
    {
        services.AddScoped<IConsultaAuditoria, ConsultaAuditoria>();
        services.AddSingleton<IAuditoriaEventCatalog, AuditoriaEventCatalog>();

        services.AddInboxHandler<AuditoriaDbContext, ContaCriada, AuditoriaEventHandler>();
        services.AddInboxHandler<AuditoriaDbContext, ApiKeyRotacionada, AuditoriaEventHandler>();
        services.AddInboxHandler<AuditoriaDbContext, ApiKeyRevogada, AuditoriaEventHandler>();
        services.AddInboxHandler<AuditoriaDbContext, WebhookConfigAlterada, AuditoriaEventHandler>();
        services.AddInboxHandler<AuditoriaDbContext, PlanoAlterado, AuditoriaEventHandler>();
        services.AddInboxHandler<AuditoriaDbContext, ContaSuspensa, AuditoriaEventHandler>();

        return services;
    }
}
