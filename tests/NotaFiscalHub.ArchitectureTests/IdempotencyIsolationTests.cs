using NetArchTest.Rules;
using NotaFiscalHub.BuildingBlocks.Idempotency;

namespace NotaFiscalHub.ArchitectureTests;

/// <summary>
/// T-B4 (spec B4, critério de aceite 13) — <c>BuildingBlocks.Idempotency</c> é kernel puro: nenhum módulo
/// de negócio deveria fazer o middleware depender de um contrato/tipo específico de módulo, o que
/// acoplaria o kernel a uma decisão de domínio de um módulo só.
/// </summary>
public class IdempotencyIsolationTests
{
    [Fact]
    public void Idempotency_NaoReferenciaNenhumModuloDeNegocio()
    {
        var result = Types.InAssembly(typeof(IdempotencyEndpointFilter).Assembly)
            .ShouldNot().HaveDependencyOnAny("NotaFiscalHub.Modules")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }
}
