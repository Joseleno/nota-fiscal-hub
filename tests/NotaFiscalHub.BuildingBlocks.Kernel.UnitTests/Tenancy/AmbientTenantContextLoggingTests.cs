using Microsoft.Extensions.Logging;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;
using Xunit;

namespace NotaFiscalHub.BuildingBlocks.Kernel.UnitTests.Tenancy;

/// <summary>
/// Prova que <c>BeginSystemScope</c> emite um log estruturado no bypass — o mecanismo de auditoria da
/// spec B2 (passo 7): todo uso do bypass fica registrado com motivo/origem, nunca silencioso.
/// </summary>
public class AmbientTenantContextLoggingTests
{
    [Fact]
    public void BeginSystemScope_EmiteLogTenantScopeBypassed()
    {
        var sink = new InMemorySink<AmbientTenantContext>();
        var context = new AmbientTenantContext(sink);

        using (context.BeginSystemScope(motivo: "seed", origem: "migration"))
        {
        }

        Assert.Contains(sink.Eventos, e => e.MessagemFormatada.Contains("TenantScopeBypassed"));
        Assert.Contains(sink.Eventos, e => e.MessagemFormatada.Contains("seed") && e.MessagemFormatada.Contains("migration"));
    }

    /// <summary>
    /// <see cref="ILogger{TCategoryName}"/> em memória: registra a mensagem já formatada de cada
    /// chamada de log, sem depender de nenhum provider/sink externo (Serilog, etc.) — o kernel não
    /// tem opinião sobre o provider de logging da aplicação hospedeira.
    /// </summary>
    private sealed class InMemorySink<T> : ILogger<T>
    {
        public List<EventoDeLog> Eventos { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Eventos.Add(new EventoDeLog(logLevel, formatter(state, exception)));
        }

        public sealed record EventoDeLog(LogLevel Nivel, string MessagemFormatada);
    }
}
