using System.Collections.Concurrent;
using NotaFiscalHub.BuildingBlocks.Messaging.Abstractions;

namespace NotaFiscalHub.BuildingBlocks.Messaging;

/// <summary>
/// Registro por módulo (um <see cref="OutboxTypeRegistry"/> por <c>AddOutboxInbox&lt;TDbContext&gt;</c>)
/// de: (a) tipos de evento conhecidos (para desserialização — tipo desconhecido é <c>Poison</c> imediato,
/// spec B3 passo 5), (b) handlers inscritos por tipo de evento (<c>AddInboxHandler&lt;TEvento,THandler&gt;</c>),
/// (c) tipos registrados como "evento de plataforma" — únicos autorizados a ter <c>ContaId = null</c>
/// (<c>AddEventoDePlataforma&lt;TEvento&gt;()</c>, spec B3 passo 4).
/// </summary>
public sealed class OutboxTypeRegistry
{
    private readonly ConcurrentDictionary<string, Type> _tiposPorNome = new();
    private readonly ConcurrentDictionary<Type, List<Type>> _handlersPorTipoDeEvento = new();
    private readonly ConcurrentDictionary<Type, byte> _eventosDePlataforma = new();

    /// <summary>
    /// Aplica, na construção, todos os registros pendentes capturados por <c>AddInboxHandler</c>/
    /// <c>AddEventoDePlataforma</c> em tempo de <c>ConfigureServices</c> — ver <see cref="IStartupRegistroDeHandler"/>.
    /// </summary>
    public OutboxTypeRegistry(IEnumerable<IStartupRegistroDeHandler>? registrosPendentes = null)
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
