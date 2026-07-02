using Mediator;
using Shouldly;
using VisuFiscalHub.Infrastructure.Jobs;

namespace VisuFiscalHub.Tests.Infrastructure.Jobs;

public class OutboxRelayJobResolveTypeTests
{
    private static readonly string ValidEventTypeName =
        typeof(VisuFiscalHub.Domain.Events.DocumentoFiscalAutorizadoEvent).AssemblyQualifiedName!;

    [Fact]
    public void ResolveEventType_TipoValidoINotification_RetornaTipo()
    {
        var result = OutboxRelayJob.ResolveEventType(ValidEventTypeName);

        result.ShouldNotBeNull();
        typeof(INotification).IsAssignableFrom(result).ShouldBeTrue();
    }

    [Fact]
    public void ResolveEventType_TipoExistenteSemINotification_RetornaNull()
    {
        var result = OutboxRelayJob.ResolveEventType(typeof(string).AssemblyQualifiedName!);

        result.ShouldBeNull();
    }

    [Fact]
    public void ResolveEventType_TipoDesconhecido_RetornaNull()
    {
        var result = OutboxRelayJob.ResolveEventType("Namespace.Inexistente.TipoFantasma, AssemblyFantasma");

        result.ShouldBeNull();
    }

    [Fact]
    public void ResolveEventType_StringVazia_RetornaNull()
    {
        var result = OutboxRelayJob.ResolveEventType(string.Empty);

        result.ShouldBeNull();
    }

    [Fact]
    public void ResolveEventType_StringNula_RetornaNull()
    {
        var result = OutboxRelayJob.ResolveEventType(null!);

        result.ShouldBeNull();
    }
}
