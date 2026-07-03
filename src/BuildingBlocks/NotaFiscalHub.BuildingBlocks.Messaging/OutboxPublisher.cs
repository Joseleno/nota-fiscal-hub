using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;
using NotaFiscalHub.BuildingBlocks.Messaging.Abstractions;

namespace NotaFiscalHub.BuildingBlocks.Messaging;

/// <summary>
/// Publica um <see cref="EventoIntegracao"/> na Outbox usando o MESMO <c>DbContext</c>/transação do
/// agregado corrente (spec B3 passo 3): <see cref="Publicar{T}"/> apenas adiciona a linha ao
/// <see cref="DbContext.ChangeTracker"/> — é <c>SaveChangesAsync</c> do chamador (dentro da transação do
/// agregado) que persiste evento e efeito de domínio atomicamente. Publicar fora de uma transação ativa
/// lança <see cref="InvalidOperationException"/>: nunca existe publicação "solta".
/// </summary>
public sealed class OutboxPublisher<TDbContext>(TDbContext db, ITenantContext tenantContext, OutboxTypeRegistry<TDbContext> registry)
    : IOutboxPublisher
    where TDbContext : DbContext
{
    public void Publicar<T>(T evento) where T : EventoIntegracao
    {
        ArgumentNullException.ThrowIfNull(evento);

        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                $"{nameof(OutboxPublisher<TDbContext>)}.{nameof(Publicar)} exige uma transação ativa no " +
                $"{typeof(TDbContext).Name} corrente — publicar fora de transação quebraria a atomicidade " +
                "entre o agregado e o evento de integração (spec B3 passo 3).");
        }

        ValidarContaIdContraTenantAmbiente(evento);

        var tipoEvento = TipoEventoDe(typeof(T));
        registry.RegistrarTipoDeEvento(tipoEvento, typeof(T));

        var mensagem = new OutboxMessage
        {
            Id = evento.MessageId,
            TipoEvento = tipoEvento,
            Payload = JsonSerializer.Serialize(evento, typeof(T)),

            // O ContaId do EVENTO (definido pelo autor do evento, tipicamente via
            // `ContaId = tenantContext.ContaId` no call-site de dentro de um handler tenant-scoped, OU
            // deliberadamente omitido/null para um evento registrado via AddEventoDePlataforma<T>()) é
            // SEMPRE autoritativo. Nunca "completar" com o ITenantContext ambiente aqui: um evento de
            // plataforma publicado a partir de dentro de um escopo de tenant (ex.: um job de manutenção
            // disparado por um handler tenant-scoped) precisa continuar com ContaId = null — do
            // contrário o dispatcher promoveria silenciosamente um evento de plataforma para
            // tenant-scoped, o oposto do que a whitelist AddEventoDePlataforma garante.
            ContaId = evento.ContaId,

            CorrelationId = CorrelationIdAtual(),
            OcorridoEm = evento.OcorridoEm,
            Status = OutboxStatus.Pendente,
            Tentativas = 0,
            ProximaTentativaEm = DateTimeOffset.UtcNow,
        };

        db.Set<OutboxMessage>().Add(mensagem);
    }

    /// <summary>Ver <see cref="OutboxDispatchRules.ContaIdDoEventoDivergeDoTenantAmbiente"/> para a regra em si.</summary>
    private void ValidarContaIdContraTenantAmbiente<T>(T evento) where T : EventoIntegracao
    {
        var contaIdAmbiente = tenantContext.HasTenant && !tenantContext.IsSystemScope ? tenantContext.ContaId : Guid.Empty;

        var diverge = OutboxDispatchRules.ContaIdDoEventoDivergeDoTenantAmbiente(
            evento.ContaId, tenantContext.HasTenant, tenantContext.IsSystemScope, contaIdAmbiente);

        if (diverge)
        {
            throw new InvalidOperationException(
                $"Evento {typeof(T).Name} publicado com ContaId={evento.ContaId} dentro do escopo de " +
                $"tenant {tenantContext.ContaId} — publicar evento de uma conta diferente da conta ambiente " +
                "provavelmente é um bug do chamador.");
        }
    }

    /// <summary>
    /// Nome lógico versionado do tipo de evento (ex.: <c>NotaFiscalHub.Modules.Foo.EventoBar.v1</c>).
    /// Usa o nome completo do tipo CLR — versionamento explícito (".v1") fica a cargo do autor do evento
    /// quando quiser introduzir uma nova versão incompatível (tipo CLR novo).
    /// </summary>
    private static string TipoEventoDe(Type tipoEvento) => $"{tipoEvento.FullName}.v1";

    /// <summary>
    /// Interface provisória de CorrelationId (dívida registrada — Tarefa 7 ainda não rodou):
    /// <c>Activity.Current?.RootId</c>, com fallback para um GUID novo quando não há Activity ativa.
    /// Ver task-3-report.md, seção "Dívida técnica".
    /// </summary>
    private static string CorrelationIdAtual() => Activity.Current?.RootId ?? Guid.NewGuid().ToString("N");
}
