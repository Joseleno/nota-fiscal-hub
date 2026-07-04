using NotaFiscalHub.BuildingBlocks.Messaging.Abstractions;

namespace NotaFiscalHub.BuildingBlocks.Auditoria.Contracts;

/// <summary>
/// Catálogo OPT-IN de eventos auditáveis (spec B5 §Abordagem passo 1/6). Evento de integração fora do
/// catálogo é ignorado silenciosamente por <see cref="TryMap"/> (retorna <see langword="false"/>, nunca
/// lança) — não envenena o inbox (critério de aceite 7). Fases 1+ estendem o catálogo ao adicionar novos
/// eventos auditáveis; a lista fechada de tipos conhecidos vive na implementação (<c>AuditoriaEventCatalog</c>),
/// nunca aqui (contrato não conhece tipos concretos de módulo).
/// </summary>
public interface IAuditoriaEventCatalog
{
    bool TryMap(EventoIntegracao evento, out RegistroAuditoriaNovo registro);
}
