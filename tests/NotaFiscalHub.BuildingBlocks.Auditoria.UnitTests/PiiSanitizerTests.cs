using NotaFiscalHub.BuildingBlocks.Auditoria;
using Xunit;

namespace NotaFiscalHub.BuildingBlocks.Auditoria.UnitTests;

/// <summary>
/// Spec B5 §Abordagem passo 2: sanitizador remove chaves da denylist (case-insensitive) antes da
/// persistência, sinalizando via <c>alertouWarn</c> — nunca ecoando o valor sensível (a mensagem de log em
/// si é responsabilidade do chamador, <c>AuditoriaEventHandler</c>, não deste método puro).
/// </summary>
public class PiiSanitizerTests
{
    [Theory]
    [InlineData("cpf")]
    [InlineData("CPF")]
    [InlineData("xml")]
    [InlineData("XmL")]
    [InlineData("senha")]
    [InlineData("cnpjConsumidor")]
    [InlineData("nome")]
    [InlineData("pfx")]
    [InlineData("csc")]
    [InlineData("token")]
    [InlineData("secret")]
    public void Sanitizar_ChaveDaDenylist_RemoveEEmiteWarn(string chaveSensivel)
    {
        var payload = new Dictionary<string, object?> { [chaveSensivel] = "valor-sensivel", ["notaId"] = "123" };

        var resultado = PiiSanitizer.Sanitizar(payload, out var alertouWarn);

        Assert.False(resultado.ContainsKey(chaveSensivel));
        Assert.True(resultado.ContainsKey("notaId"));
        Assert.True(alertouWarn);
    }

    [Fact]
    public void Sanitizar_SemChaveDaDenylist_NaoAlteraPayloadNemAlerta()
    {
        var payload = new Dictionary<string, object?> { ["notaId"] = "123", ["status"] = "autorizada" };

        var resultado = PiiSanitizer.Sanitizar(payload, out var alertouWarn);

        Assert.Equal(2, resultado.Count);
        Assert.False(alertouWarn);
    }

    [Fact]
    public void Sanitizar_NaoModificaODicionarioOriginal()
    {
        var payload = new Dictionary<string, object?> { ["cpf"] = "12345678900", ["notaId"] = "123" };

        PiiSanitizer.Sanitizar(payload, out _);

        Assert.True(payload.ContainsKey("cpf")); // dicionário original intacto — resultado é uma cópia.
    }

    [Fact]
    public void Sanitizar_ValorRemovidoNuncaApareceNoResultado_MesmoComoSubstring()
    {
        var payload = new Dictionary<string, object?> { ["cpf"] = "99988877766", ["notaId"] = "abc" };

        var resultado = PiiSanitizer.Sanitizar(payload, out _);

        Assert.DoesNotContain(resultado.Values, v => v is string s && s.Contains("99988877766"));
    }
}
