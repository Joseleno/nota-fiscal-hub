using System.Reflection;
using Mediator;
using Shouldly;
using VisuFiscalHub.Infrastructure.Jobs;

namespace VisuFiscalHub.Tests.Infrastructure.Jobs;

public class OutboxRelayJobResolveTypeTests
{
    private static readonly string ValidEventTypeName =
        typeof(VisuFiscalHub.Domain.Events.DocumentoFiscalAutorizadoEvent).AssemblyQualifiedName!;

    private static Type? InvokeResolveEventType(string typeName)
    {
        var method = typeof(OutboxRelayJob)
            .GetMethod("ResolveEventType", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (Type?)method.Invoke(null, [typeName]);
    }

    [Fact]
    public void ResolveEventType_TipoValidoINotification_RetornaTipo()
    {
        var result = InvokeResolveEventType(ValidEventTypeName);

        result.ShouldNotBeNull();
        typeof(INotification).IsAssignableFrom(result).ShouldBeTrue();
    }

    [Fact]
    public void ResolveEventType_TipoExistenteSemINotification_RetornaNull()
    {
        var typeName = typeof(string).AssemblyQualifiedName!;

        var result = InvokeResolveEventType(typeName);

        result.ShouldBeNull();
    }

    [Fact]
    public void ResolveEventType_TipoDesconhecido_RetornaNull()
    {
        var result = InvokeResolveEventType("Namespace.Inexistente.TipoFantasma, AssemblyFantasma");

        result.ShouldBeNull();
    }

    [Fact]
    public void ResolveEventType_StringVazia_RetornaNull()
    {
        var result = InvokeResolveEventType(string.Empty);

        result.ShouldBeNull();
    }

    [Fact]
    public void ResolveEventType_StringNula_RetornaNull()
    {
        var result = InvokeResolveEventType(null!);

        result.ShouldBeNull();
    }
}
