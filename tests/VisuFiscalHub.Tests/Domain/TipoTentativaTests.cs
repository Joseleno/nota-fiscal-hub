using Shouldly;
using VisuFiscalHub.Domain.Enums;

namespace VisuFiscalHub.Tests.Domain;

public class TipoTentativaTests
{
    [Fact]
    public void TipoTentativa_NaoDeveTerRetry()
    {
        var names = Enum.GetNames<TipoTentativa>();
        names.ShouldNotContain("Retry");
    }

    [Fact]
    public void TipoTentativa_Valor3_NaoDeveEstarDefinido()
    {
        Enum.IsDefined(typeof(TipoTentativa), 3).ShouldBeFalse();
    }

    [Fact]
    public void TipoTentativa_Cancelamento_DeveSerValor4()
    {
        ((int)TipoTentativa.Cancelamento).ShouldBe(4);
    }

    [Fact]
    public void TipoTentativa_Envio_DeveSerValor1()
    {
        ((int)TipoTentativa.Envio).ShouldBe(1);
    }

    [Fact]
    public void TipoTentativa_Consulta_DeveSerValor2()
    {
        ((int)TipoTentativa.Consulta).ShouldBe(2);
    }
}
