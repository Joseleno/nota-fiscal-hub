using NotaFiscalHub.BuildingBlocks.Messaging;
using NotaFiscalHub.BuildingBlocks.Messaging.Abstractions;

namespace NotaFiscalHub.BuildingBlocks.Messaging.UnitTests;

/// <summary>
/// Política de evento órfão (spec B3 passo 5 — a lição do legado MSG0005: "5 eventos sem handler
/// engolidos pelo OutboxRelayJob central"). Cobre, isoladamente e sem I/O: (a) a regra de decisão
/// (<see cref="OutboxDispatchRules.Classificar"/>) nunca resulta em "Executar" com zero handlers — já
/// coberto em detalhe por <c>OutboxDispatchRulesTests</c>, revalidado aqui do ponto de vista específico
/// de "evento órfão"; (b) o <see cref="OutboxTypeRegistry"/> reporta corretamente zero handlers para um
/// tipo nunca inscrito, e a lista cresce independentemente por tipo de evento (inscrever um handler para
/// <c>EventoA</c> não "vaza" para <c>EventoB</c>).
/// </summary>
public class OrphanEventPolicyTests
{
    [Fact]
    public void Registry_TipoDeEventoNuncaInscrito_HandlersRegistrados_RetornaListaVazia()
    {
        var registry = new OutboxTypeRegistry();

        var handlers = registry.HandlersRegistrados(typeof(EventoA));

        Assert.Empty(handlers);
    }

    [Fact]
    public void Registry_HandlerInscritoParaOutroTipo_NaoVazaParaTipoOrfao()
    {
        var registry = new OutboxTypeRegistry();
        registry.RegistrarHandler<EventoA, HandlerDeA>();

        var handlersDeA = registry.HandlersRegistrados(typeof(EventoA));
        var handlersDeB = registry.HandlersRegistrados(typeof(EventoB));

        Assert.Single(handlersDeA);
        Assert.Empty(handlersDeB);
    }

    [Fact]
    public void Registry_TipoDeEventoNuncaRegistrado_ResolverTipoClr_RetornaNull()
    {
        // Tipo nunca visto por RegistrarTipoDeEvento (ex.: OutboxPublisher.Publicar nunca foi chamado
        // para esse tipo neste processo) — o dispatcher trata isso como Poison imediato (tipo não
        // desserializável), nunca como "ignorar e seguir em frente".
        var registry = new OutboxTypeRegistry();

        var tipo = registry.ResolverTipoClr("tipo.jamais.publicado.v1");

        Assert.Null(tipo);
    }

    [Fact]
    public void Classificar_EventoOrfao_ContaIdPreenchido_ZeroHandlers_RetornaSemHandler_NuncaExecutar()
    {
        var decisao = OutboxDispatchRules.Classificar(Guid.NewGuid(), ehEventoDePlataforma: false, totalDeHandlers: 0);

        Assert.Equal(TipoDeDecisaoDeDispatch.SemHandler, decisao.Tipo);
        Assert.NotEqual(TipoDeDecisaoDeDispatch.Executar, decisao.Tipo);
    }

    [Fact]
    public void Classificar_UmHandlerRegistrado_NaoEhTratadoComoOrfao()
    {
        var decisao = OutboxDispatchRules.Classificar(Guid.NewGuid(), ehEventoDePlataforma: false, totalDeHandlers: 1);

        Assert.NotEqual(TipoDeDecisaoDeDispatch.SemHandler, decisao.Tipo);
    }

    private sealed record EventoA : EventoIntegracao;
    private sealed record EventoB : EventoIntegracao;

    private sealed class HandlerDeA : IInboxHandler<EventoA>
    {
        public Task HandleAsync(EventoA evento, MensagemContexto ctx, CancellationToken ct) => Task.CompletedTask;
    }
}
