namespace NotaFiscalHub.BuildingBlocks.Messaging.Abstractions;

/// <summary>
/// Publica eventos de integração na Outbox do módulo. Implementação (<c>Messaging</c>) usa o mesmo
/// <c>DbContext</c>/transação do agregado corrente (mesma <c>SaveChanges</c>) — publicar fora de uma
/// transação ativa é erro (nunca publica "solto"). Registrado por módulo, amarrado ao DbContext do módulo.
/// </summary>
public interface IOutboxPublisher
{
    void Publicar<T>(T evento) where T : EventoIntegracao;
}
