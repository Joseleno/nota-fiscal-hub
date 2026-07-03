using Microsoft.EntityFrameworkCore;
using NotaFiscalHub.BuildingBlocks.Messaging.Abstractions;

namespace NotaFiscalHub.BuildingBlocks.Messaging;

/// <summary>
/// Registro pendente capturado em tempo de <c>ConfigureServices</c> (via <c>AddInboxHandler</c>/
/// <c>AddEventoDePlataforma</c>) e aplicado ao <see cref="OutboxTypeRegistry{TDbContext}"/> DO MÓDULO
/// <typeparamref name="TDbContext"/> assim que este é resolvido pela primeira vez — necessário porque não
/// existe <c>IServiceProvider</c> ainda no momento da chamada a
/// <c>AddInboxHandler&lt;TDbContext,TEvento,THandler&gt;()</c> em <c>Program.cs</c>.
///
/// Parametrizado por <typeparamref name="TDbContext"/> pelo mesmo motivo do registry: sem essa amarração,
/// <c>IEnumerable&lt;IStartupRegistroDeHandler&gt;</c> resolveria TODOS os registros pendentes de TODOS os
/// módulos do processo (ex.: o Worker, que referencia vários módulos no mesmo <c>IServiceCollection</c>),
/// vazando handlers/eventos de plataforma de um módulo para o registry de outro.
/// </summary>
public interface IStartupRegistroDeHandler<TDbContext> where TDbContext : DbContext
{
    void AplicarEm(OutboxTypeRegistry<TDbContext> registry);
}

public sealed class StartupRegistroDeHandler<TDbContext, TEvento, THandler> : IStartupRegistroDeHandler<TDbContext>
    where TDbContext : DbContext
    where TEvento : EventoIntegracao
    where THandler : class, IInboxHandler<TEvento>
{
    public void AplicarEm(OutboxTypeRegistry<TDbContext> registry) => registry.RegistrarHandler<TEvento, THandler>();
}

public sealed class StartupRegistroDeEventoDePlataforma<TDbContext, TEvento> : IStartupRegistroDeHandler<TDbContext>
    where TDbContext : DbContext
    where TEvento : EventoIntegracao
{
    public void AplicarEm(OutboxTypeRegistry<TDbContext> registry) => registry.RegistrarEventoDePlataforma<TEvento>();
}
