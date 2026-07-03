using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using NotaFiscalHub.BuildingBlocks.Messaging.Abstractions;

namespace NotaFiscalHub.BuildingBlocks.Messaging;

/// <summary>
/// Registro por módulo (um <see cref="OutboxTypeRegistry{TDbContext}"/> por <c>AddOutboxInbox&lt;TDbContext&gt;</c>)
/// de: (a) tipos de evento conhecidos (para desserialização — tipo desconhecido é <c>Poison</c> imediato,
/// spec B3 passo 5), (b) handlers inscritos por tipo de evento (<c>AddInboxHandler&lt;TDbContext,TEvento,THandler&gt;</c>),
/// (c) tipos registrados como "evento de plataforma" — únicos autorizados a ter <c>ContaId = null</c>
/// (<c>AddEventoDePlataforma&lt;TDbContext,TEvento&gt;()</c>, spec B3 passo 4).
///
/// Parametrizado por <typeparamref name="TDbContext"/>: cada módulo (cada <c>AddOutboxInbox&lt;TDbContext&gt;</c>)
/// resolve uma instância singleton DISTINTA — <c>OutboxTypeRegistry&lt;ModuloADbContext&gt;</c> e
/// <c>OutboxTypeRegistry&lt;ModuloBDbContext&gt;</c> nunca compartilham tabela de tipos/handlers/eventos de
/// plataforma, mesmo quando registrados no MESMO <c>IServiceCollection</c> (ex.: o Worker, que referencia
/// vários módulos). Antes desta parametrização, o tipo era não-genérico e <c>TryAddSingleton</c> fazia com
/// que só o PRIMEIRO módulo a chamar <c>AddOutboxInbox</c> "vencesse" — os demais silenciosamente
/// compartilhavam essa instância, recriando o antipadrão de "outbox central" que a spec B3 proíbe
/// explicitamente ("nunca serviço central").
/// </summary>
public sealed class OutboxTypeRegistry<TDbContext> where TDbContext : DbContext
{
    private readonly ConcurrentDictionary<string, Type> _tiposPorNome = new();
    private readonly ConcurrentDictionary<Type, List<Type>> _handlersPorTipoDeEvento = new();
    private readonly ConcurrentDictionary<Type, byte> _eventosDePlataforma = new();

    /// <summary>
    /// Aplica, na construção, todos os registros pendentes capturados por <c>AddInboxHandler</c>/
    /// <c>AddEventoDePlataforma</c> em tempo de <c>ConfigureServices</c> para ESTE <typeparamref name="TDbContext"/>
    /// — ver <see cref="IStartupRegistroDeHandler{TDbContext}"/>.
    /// </summary>
    public OutboxTypeRegistry(IEnumerable<IStartupRegistroDeHandler<TDbContext>>? registrosPendentes = null)
    {
        foreach (var registro in registrosPendentes ?? [])
        {
            registro.AplicarEm(this);
        }
    }

    public void RegistrarTipoDeEvento(string tipoEvento, Type tipoClr) =>
        _tiposPorNome[tipoEvento] = tipoClr;

    public Type? ResolverTipoClr(string tipoEvento) =>
        _tiposPorNome.GetValueOrDefault(tipoEvento);

    public void RegistrarHandler<TEvento, THandler>()
        where TEvento : EventoIntegracao
        where THandler : class, IInboxHandler<TEvento>
    {
        var lista = _handlersPorTipoDeEvento.GetOrAdd(typeof(TEvento), _ => []);
        lock (lista)
        {
            if (!lista.Contains(typeof(THandler)))
                lista.Add(typeof(THandler));
        }
    }

    public IReadOnlyList<Type> HandlersRegistrados(Type tipoDeEvento) =>
        _handlersPorTipoDeEvento.TryGetValue(tipoDeEvento, out var lista) ? lista : [];

    public void RegistrarEventoDePlataforma<TEvento>() where TEvento : EventoIntegracao =>
        _eventosDePlataforma[typeof(TEvento)] = 0;

    public bool EhEventoDePlataforma(Type tipoDeEvento) =>
        _eventosDePlataforma.ContainsKey(tipoDeEvento);
}
