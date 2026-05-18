namespace VisuFiscalHub.Tests.Helpers;

/// <summary>
/// <see cref="TimeProvider"/> determinístico para testes: retorna sempre o mesmo instante.
/// </summary>
internal sealed class FixedTimeProvider(DateTimeOffset fixedNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => fixedNow;
}
