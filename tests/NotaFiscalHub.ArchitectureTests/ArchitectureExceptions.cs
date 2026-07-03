namespace NotaFiscalHub.ArchitectureTests;

/// <summary>
/// Uma exceção documentada a uma regra de fronteira (design §2.3/§2.5). Exceções são sempre por PAR
/// origem→destino, nunca por módulo inteiro — abrir uma exceção não relaxa a regra para o resto do
/// módulo. Nova exceção = PR alterando este arquivo + registro em <c>docs/decisions.md</c>.
/// </summary>
/// <param name="Regra">Identificador da regra (T1-T7) à qual esta exceção se aplica.</param>
/// <param name="Origem">Assembly/módulo de origem da referência excepcional.</param>
/// <param name="Destino">Assembly/módulo de destino permitido excepcionalmente.</param>
/// <param name="Justificativa">Por que a exceção é necessária — nunca pode ficar em branco.</param>
/// <param name="Data">Data em que a exceção foi registrada.</param>
/// <param name="DecisaoRef">Referência rastreável (ex.: <c>D-2026-07-01-xx</c> ou seção do design) que aprovou a exceção.</param>
public sealed record ExcecaoArquitetura(
    string Regra, string Origem, string Destino, string Justificativa, DateOnly Data, string DecisaoRef);

/// <summary>
/// Allowlist única de exceções às regras de fronteira. Crescimento silencioso desta lista anula a
/// garantia das regras T1-T7 — por isso <see cref="ArchitectureExceptionsGuardTests.TodaExcecao_TemJustificativaEDecisaoRef"/>
/// falha o build se qualquer entrada estiver sem justificativa ou sem referência de decisão.
/// </summary>
public static class ArchitectureExceptions
{
    public static readonly IReadOnlyList<ExcecaoArquitetura> Todas =
    [
        new("T1", ModuleNames.BuildingBlocks, $"{ModuleNames.ContasPlanos}.Contracts",
            "Middleware de autenticacao consome credencial/consumo/webhook-config (design SS2.5)",
            new DateOnly(2026, 7, 2), "docs/superpowers/specs/2026-07-01-nota-fiscal-hub-produto-design.md#2.5"),
    ];
}

/// <summary>
/// Teste-guardião do processo de exceção (Step 9 da Tarefa 6): toda entrada de
/// <see cref="ArchitectureExceptions.Todas"/> precisa ter justificativa e referência de decisão
/// preenchidas — sem isso, a allowlist vira ralo silencioso (spec B6, "Riscos").
/// </summary>
public class ArchitectureExceptionsGuardTests
{
    [Fact]
    public void TodaExcecao_TemJustificativaEDecisaoRef()
    {
        Assert.NotEmpty(ArchitectureExceptions.Todas);

        Assert.All(ArchitectureExceptions.Todas, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Justificativa), $"{e.Regra} {e.Origem}->{e.Destino}: Justificativa vazia.");
            Assert.False(string.IsNullOrWhiteSpace(e.DecisaoRef), $"{e.Regra} {e.Origem}->{e.Destino}: DecisaoRef vazia.");
        });
    }

    [Fact]
    public void SeedInicial_ContemExatamenteUmaEntrada()
    {
        Assert.Single(ArchitectureExceptions.Todas);

        var unica = ArchitectureExceptions.Todas[0];
        Assert.Equal("T1", unica.Regra);
        Assert.Equal(ModuleNames.BuildingBlocks, unica.Origem);
        Assert.Equal($"{ModuleNames.ContasPlanos}.Contracts", unica.Destino);
    }
}
