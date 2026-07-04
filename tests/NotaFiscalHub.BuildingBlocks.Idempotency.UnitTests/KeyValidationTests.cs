using System.Text;
using NotaFiscalHub.BuildingBlocks.Idempotency;

namespace NotaFiscalHub.BuildingBlocks.Idempotency.UnitTests;

/// <summary>
/// Validação da <c>Idempotency-Key</c> (spec B4 §Abordagem passo 3, "1–255 chars visíveis") e do hash do
/// corpo cru (spec B4 §Abordagem passo 3, "computa SHA-256" sobre os BYTES, não sobre uma forma
/// canonicalizada) — regras puras, sem I/O, exercitadas sem depender de PostgreSQL real.
/// </summary>
public class KeyValidationTests
{
    [Theory]
    [InlineData("", false)]
    [InlineData("a", true)]
    [InlineData("chave-valida-123", true)]
    public void ValidarKey_RetornaConformeTamanhoEChars(string key, bool esperadoValido)
    {
        Assert.Equal(esperadoValido, IdempotencyKeyValidator.EhValida(key));
    }

    [Fact]
    public void ValidarKey_Exatamente255Chars_EhValida()
    {
        var key = new string('a', 255);
        Assert.True(IdempotencyKeyValidator.EhValida(key));
    }

    [Fact]
    public void ValidarKey_256Chars_EhInvalida()
    {
        var key = new string('a', 256);
        Assert.False(IdempotencyKeyValidator.EhValida(key));
    }

    [Fact]
    public void ValidarKey_ContemCaractereDeControle_EhInvalida()
    {
        // Tab (0x09) não é caractere "visível" — a spec exige chars visíveis (1–255).
        Assert.False(IdempotencyKeyValidator.EhValida("chave\tinvalida"));
    }

    [Fact]
    public void ValidarKey_Nula_EhInvalida()
    {
        Assert.False(IdempotencyKeyValidator.EhValida(null!));
    }

    [Fact]
    public void CalcularHash_CorposComWhitespaceDiferente_GeramHashesDistintos()
    {
        var hashA = PayloadHasher.Sha256(Encoding.UTF8.GetBytes("{\"a\":1}"));
        var hashB = PayloadHasher.Sha256(Encoding.UTF8.GetBytes("{ \"a\": 1 }"));
        Assert.NotEqual(hashA, hashB);
    }

    [Fact]
    public void CalcularHash_MesmosBytes_GeraMesmoHash()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"a\":1}");
        var hashA = PayloadHasher.Sha256(bytes);
        var hashB = PayloadHasher.Sha256(bytes);
        Assert.Equal(hashA, hashB);
    }
}
