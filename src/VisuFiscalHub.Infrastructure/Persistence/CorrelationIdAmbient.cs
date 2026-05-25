namespace VisuFiscalHub.Infrastructure.Persistence;

internal static class CorrelationIdAmbient
{
    private static readonly AsyncLocal<string?> _current = new();

    public static string? Current => _current.Value;

    public static void Set(string? correlationId) => _current.Value = correlationId;
}
