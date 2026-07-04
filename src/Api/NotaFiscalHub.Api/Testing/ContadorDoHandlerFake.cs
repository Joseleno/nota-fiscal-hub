namespace NotaFiscalHub.Api.Testing;

/// <summary>
/// Contador de execuções do handler fake usado pelos testes de integração de Idempotency-Key
/// (spec B4 §Plano de testes: "verificado por contador no handler fake"). Singleton — precisa sobreviver
/// entre requisições HTTP diferentes do mesmo <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/>.
/// Só registrado/exposto em Development (mesmo guard de <c>/teste/tenant-atual</c>).
/// </summary>
public sealed class ContadorDoHandlerFake
{
    private int _total;

    public int Total => _total;

    public int Incrementar() => Interlocked.Increment(ref _total);

    public void Resetar() => Interlocked.Exchange(ref _total, 0);
}
