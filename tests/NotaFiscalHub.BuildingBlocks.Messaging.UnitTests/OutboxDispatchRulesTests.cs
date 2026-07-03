using NotaFiscalHub.BuildingBlocks.Messaging;

namespace NotaFiscalHub.BuildingBlocks.Messaging.UnitTests;

/// <summary>
/// Testes de unidade (sem I/O) para as regras de decisão do dispatcher — spec B3 passos 4/5/6. Cobrem a
/// propriedade fail-safe mais crítica da biblioteca (evento sem handler nunca vira <c>Processada</c>) e a
/// regra de escopo de tenant sem depender de PostgreSQL real (essas dependem só da lógica de
/// classificação, não da semântica transacional/lock, que é coberta pelos testes de integração).
/// </summary>
public class OutboxDispatchRulesTests
{
    private static readonly Guid ContaId = Guid.NewGuid();

    [Fact]
    public void Classificar_ZeroHandlers_RetornaSemHandler_MesmoComContaIdPreenchido()
    {
        var decisao = OutboxDispatchRules.Classificar(ContaId, ehEventoDePlataforma: false, totalDeHandlers: 0);

        Assert.Equal(TipoDeDecisaoDeDispatch.SemHandler, decisao.Tipo);
    }

    [Fact]
    public void Classificar_ZeroHandlers_NuncaRetornaExecutar()
    {
        // Propriedade fail-safe (MSG0005): não importa a combinação de ContaId/plataforma, zero handlers
        // NUNCA deve resultar em "Executar" (que levaria a Processada) — sempre SemHandler ou Poison.
        var decisaoComConta = OutboxDispatchRules.Classificar(ContaId, ehEventoDePlataforma: false, totalDeHandlers: 0);
        var decisaoSemContaNaoPlataforma = OutboxDispatchRules.Classificar(null, ehEventoDePlataforma: false, totalDeHandlers: 0);
        var decisaoPlataforma = OutboxDispatchRules.Classificar(null, ehEventoDePlataforma: true, totalDeHandlers: 0);

        Assert.NotEqual(TipoDeDecisaoDeDispatch.Executar, decisaoComConta.Tipo);
        Assert.NotEqual(TipoDeDecisaoDeDispatch.Executar, decisaoSemContaNaoPlataforma.Tipo);
        Assert.NotEqual(TipoDeDecisaoDeDispatch.Executar, decisaoPlataforma.Tipo);
    }

    [Fact]
    public void Classificar_ContaIdNulo_NaoEhPlataforma_RetornaPoisonComMensagemExata()
    {
        var decisao = OutboxDispatchRules.Classificar(null, ehEventoDePlataforma: false, totalDeHandlers: 1);

        Assert.Equal(TipoDeDecisaoDeDispatch.PoisonImediato, decisao.Tipo);
        Assert.Contains("evento tenant-scoped sem conta_id", decisao.MotivoDoPoison);
    }

    [Fact]
    public void Classificar_ContaIdPreenchido_EhPlataforma_RetornaPoison()
    {
        var decisao = OutboxDispatchRules.Classificar(ContaId, ehEventoDePlataforma: true, totalDeHandlers: 1);

        Assert.Equal(TipoDeDecisaoDeDispatch.PoisonImediato, decisao.Tipo);
        Assert.Contains("evento de plataforma com conta_id", decisao.MotivoDoPoison);
    }

    [Fact]
    public void Classificar_ContaIdPreenchido_NaoEhPlataforma_ExecutaComEscopoDeTenant()
    {
        var decisao = OutboxDispatchRules.Classificar(ContaId, ehEventoDePlataforma: false, totalDeHandlers: 1);

        Assert.Equal(TipoDeDecisaoDeDispatch.Executar, decisao.Tipo);
        Assert.Equal(ContaId, decisao.ContaIdParaEscopo);
    }

    [Fact]
    public void Classificar_ContaIdNulo_EhPlataforma_ExecutaSemEscopoDeTenant()
    {
        var decisao = OutboxDispatchRules.Classificar(null, ehEventoDePlataforma: true, totalDeHandlers: 1);

        Assert.Equal(TipoDeDecisaoDeDispatch.Executar, decisao.Tipo);
        Assert.Null(decisao.ContaIdParaEscopo);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public void ClassificarFalha_AbaixoDoLimite_VoltaParaPendenteComBackoff(int tentativasAnteriores)
    {
        var decisao = OutboxDispatchRules.ClassificarFalha(tentativasAnteriores, maxTentativas: 10, seedDeJitter: 1);

        Assert.False(decisao.EhPoison);
        Assert.Equal(tentativasAnteriores + 1, decisao.NovaTentativa);
        Assert.NotNull(decisao.Backoff);
        Assert.True(decisao.Backoff!.Value > TimeSpan.Zero);
    }

    [Fact]
    public void ClassificarFalha_DecimaTentativa_ViraPoison()
    {
        // 9 falhas anteriores + esta = 10ª tentativa = MaxTentativas → Poison (spec B3 passo 6).
        var decisao = OutboxDispatchRules.ClassificarFalha(tentativasAnteriores: 9, maxTentativas: 10, seedDeJitter: 1);

        Assert.True(decisao.EhPoison);
        Assert.Equal(10, decisao.NovaTentativa);
        Assert.Null(decisao.Backoff);
    }

    [Fact]
    public void ClassificarFalha_NonaTentativa_AindaNaoEhPoison()
    {
        var decisao = OutboxDispatchRules.ClassificarFalha(tentativasAnteriores: 8, maxTentativas: 10, seedDeJitter: 1);

        Assert.False(decisao.EhPoison);
        Assert.Equal(9, decisao.NovaTentativa);
    }
}
