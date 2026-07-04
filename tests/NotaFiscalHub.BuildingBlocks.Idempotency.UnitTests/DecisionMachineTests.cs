namespace NotaFiscalHub.BuildingBlocks.Idempotency.UnitTests;

/// <summary>
/// Máquina de decisão de <see cref="IdempotencyDecisionRules.Decidir"/> — regra pura (sem I/O) extraída de
/// <see cref="IdempotencyStore.BeginAsync"/> (spec B4 §Abordagem passo 3, mesmo espírito de
/// <c>OutboxDispatchRules</c> na Tarefa 3): dado o registro existente (ou nenhum) e o hash novo, decide
/// entre inserir, repetir a resposta, sinalizar conflito ou sinalizar corrida — SEM tocar banco. A
/// resolução real da corrida (unicidade da PK no INSERT) só é verificável com PostgreSQL real
/// (tests/NotaFiscalHub.IntegrationTests/Idempotency/ConcurrencyTests.cs).
/// </summary>
public class DecisionMachineTests
{
    [Theory]
    [InlineData("inexistente", "hash-novo", IdempotencyBeginOutcome.Inserted)]
    [InlineData("Concluida-hash-igual", "hash-igual", IdempotencyBeginOutcome.ReplayConcluida)]
    [InlineData("Concluida-hash-diferente", "hash-novo", IdempotencyBeginOutcome.ConflitoHashDiferente)]
    [InlineData("EmProcessamento-hash-igual", "hash-igual", IdempotencyBeginOutcome.CorridaEmProcessamento)]
    [InlineData("EmProcessamento-hash-diferente", "hash-novo", IdempotencyBeginOutcome.ConflitoHashDiferente)]
    public void Decidir_Retorna_Inserted_Replay_Conflito_OuCorrida(
        string estadoExistente, string hashNovo, IdempotencyBeginOutcome esperado)
    {
        var existente = FakeRegistro(estadoExistente);
        var resultado = IdempotencyDecisionRules.Decidir(existente, hashNovo, orfaoVencido: false);

        Assert.Equal(esperado, resultado.Outcome);
    }

    [Fact]
    public void Decidir_ExistenteNulo_RetornaInsertedSemRegistroExistente()
    {
        var resultado = IdempotencyDecisionRules.Decidir(existente: null, "hash-novo", orfaoVencido: false);

        Assert.Equal(IdempotencyBeginOutcome.Inserted, resultado.Outcome);
        Assert.Null(resultado.Existente);
    }

    [Fact]
    public void Decidir_EmProcessamentoOrfaoVencido_HashIgual_RetornaInsertedViaTakeover()
    {
        // Órfão vencido (criada_em mais velho que OrphanTimeout) é reaproveitado independente do hash:
        // o crash anterior nunca terminou de processar, então não há "resposta" para comparar — o novo
        // request simplesmente assume o registro (spec B4 §Abordagem passo 5).
        var existente = FakeRegistro("EmProcessamento-hash-igual");
        var resultado = IdempotencyDecisionRules.Decidir(existente, "hash-igual", orfaoVencido: true);

        Assert.Equal(IdempotencyBeginOutcome.Inserted, resultado.Outcome);
    }

    [Fact]
    public void Decidir_EmProcessamentoOrfaoVencido_HashDiferente_RetornaInsertedViaTakeover()
    {
        var existente = FakeRegistro("EmProcessamento-hash-igual");
        var resultado = IdempotencyDecisionRules.Decidir(existente, "hash-outro", orfaoVencido: true);

        Assert.Equal(IdempotencyBeginOutcome.Inserted, resultado.Outcome);
    }

    [Fact]
    public void Decidir_ConcluidaHashIgual_RetornaExistenteParaReplay()
    {
        var existente = FakeRegistro("Concluida-hash-igual");
        var resultado = IdempotencyDecisionRules.Decidir(existente, "hash-igual", orfaoVencido: false);

        Assert.Same(existente, resultado.Existente);
    }

    private static IdempotencyRecord? FakeRegistro(string estadoExistente)
    {
        // Convenção do brief (Step 4): "inexistente" → sem registro; caso contrário, o prefixo antes do
        // hífen é o IdempotencyState e o restante é o hash armazenado.
        if (estadoExistente == "inexistente")
            return null;

        var partes = estadoExistente.Split('-', 2);
        var estado = Enum.Parse<IdempotencyState>(partes[0]);
        var hashArmazenado = partes[1] switch
        {
            "hash-igual" => "hash-igual",
            "hash-diferente" => "hash-armazenado-diferente",
            _ => throw new InvalidOperationException($"Sufixo de hash desconhecido: {partes[1]}"),
        };

        return new IdempotencyRecord(
            ContaId: Guid.NewGuid(),
            Ambiente: "producao",
            Key: "chave-teste",
            Rota: "POST /v1/nfce",
            PayloadHashSha256: hashArmazenado,
            Estado: estado,
            RespostaStatus: estado == IdempotencyState.Concluida ? 200 : null,
            RespostaCorpo: estado == IdempotencyState.Concluida ? "{}" : null,
            RespostaContentType: estado == IdempotencyState.Concluida ? "application/json" : null,
            RespostaLocation: null,
            CriadaEm: DateTimeOffset.UtcNow,
            ExpiraEm: DateTimeOffset.UtcNow.AddHours(24));
    }
}
