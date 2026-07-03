using NotaFiscalHub.BuildingBlocks.Messaging.Abstractions;

namespace NotaFiscalHub.BuildingBlocks.Messaging;

/// <summary>
/// Registro pendente capturado em tempo de <c>ConfigureServices</c> (via <c>AddInboxHandler</c>/
/// <c>AddEventoDePlataforma</c>) e aplicado ao <see cref="OutboxTypeRegistry"/> do processo assim que
/// este é resolvido pela primeira vez — necessário porque não existe <c>IServiceProvider</c> ainda no
/// momento da chamada a <c>AddInboxHandler&lt;TEvento,THandler&gt;()</c> em <c>Program.cs</c>.
/// </summary>
public interface IStartupRegistroDeHandler
{
    void AplicarEm(OutboxTypeRegistry registry);
}

public sealed class StartupRegistroDeHandler<TEvento, THandler> : IStartupRegistroDeHandler
    where TEvento : EventoIntegracao
    where THandler : class, IInboxHandler<TEvento>
{
    public void AplicarEm(OutboxTypeRegistry registry) => registry.RegistrarHandler<TEvento, THandler>();
}

public sealed class StartupRegistroDeEventoDePlataforma<TEvento> : IStartupRegistroDeHandler
    where TEvento : EventoIntegracao
{
    public void AplicarEm(OutboxTypeRegistry registry) => registry.RegistrarEventoDePlataforma<TEvento>();
}
