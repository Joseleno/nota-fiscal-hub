using NotaFiscalHub.BuildingBlocks.Messaging.Abstractions;
using NotaFiscalHub.BuildingBlocks.Messaging.TestKit;

namespace NotaFiscalHub.BuildingBlocks.Messaging.UnitTests;

public class OutboxTestHarnessTests
{
    [Fact]
    public void MontarSequenciaDeEntregas_MesmaSeed_ProduzMesmaSequencia()
    {
        var eventos = new[] { new EventoDeTeste(), new EventoDeTeste(), new EventoDeTeste() };

        var harness1 = new OutboxTestHarness(seed: 55);
        var harness2 = new OutboxTestHarness(seed: 55);

        var sequencia1 = harness1.MontarSequenciaDeEntregas(eventos);
        var sequencia2 = harness2.MontarSequenciaDeEntregas(eventos);

        Assert.Equal(
            sequencia1.Select(e => (e.Evento.MessageId, e.Tentativa)),
            sequencia2.Select(e => (e.Evento.MessageId, e.Tentativa)));
    }

    [Fact]
    public void MontarSequenciaDeEntregas_TodoEventoApareceAoMenosUmaVez()
    {
        var eventos = Enumerable.Range(0, 10).Select(_ => new EventoDeTeste()).ToArray();
        var harness = new OutboxTestHarness(seed: 1);

        var sequencia = harness.MontarSequenciaDeEntregas(eventos);

        foreach (var evento in eventos)
        {
            Assert.Contains(sequencia, e => e.Evento.MessageId == evento.MessageId);
        }
    }

    [Fact]
    public void MontarSequenciaDeEntregas_RespeitaMaxDuplicatas()
    {
        var eventos = Enumerable.Range(0, 20).Select(_ => new EventoDeTeste()).ToArray();
        var harness = new OutboxTestHarness(seed: 2);

        var sequencia = harness.MontarSequenciaDeEntregas(eventos, maxDuplicatas: 3);

        var contagens = sequencia.GroupBy(e => e.Evento.MessageId).Select(g => g.Count());
        Assert.All(contagens, c => Assert.InRange(c, 1, 3));
    }

    [Fact]
    public void MontarSequenciaDeEntregas_ComMuitosEventos_ProduzAlgumaReordenacao()
    {
        // Com >= 2 eventos e uma seed "ativa", o embaralhamento deve, na prática, produzir uma ordem
        // diferente da ordem de publicação em pelo menos uma das duas seeds testadas — não é garantido
        // matematicamente para toda seed (poderia coincidir), mas com 2 seeds distintas a chance de as
        // DUAS coincidirem com a ordem original é desprezível o suficiente para ancorar o teste.
        var eventos = Enumerable.Range(0, 8).Select(_ => new EventoDeTeste()).ToArray();
        var ordemOriginal = eventos.Select(e => e.MessageId).ToList();

        var reordenouEmAlgumaSeed = Enumerable.Range(1, 5).Any(seed =>
        {
            var harness = new OutboxTestHarness(seed);
            var sequencia = harness.MontarSequenciaDeEntregas(eventos, maxDuplicatas: 1);
            return !sequencia.Select(e => e.Evento.MessageId).SequenceEqual(ordemOriginal);
        });

        Assert.True(reordenouEmAlgumaSeed);
    }

    [Fact]
    public async Task DespacharComDuplicataEForaDeOrdemAsync_ChamaHandlerParaCadaEntregaPlanejada()
    {
        var eventos = new[] { new EventoDeTeste(), new EventoDeTeste() };
        var harness = new OutboxTestHarness(seed: 9);
        var sequenciaEsperada = harness.MontarSequenciaDeEntregas(eventos);

        var chamadas = new List<Guid>();
        var harnessParaDespacho = new OutboxTestHarness(seed: 9);

        await harnessParaDespacho.DespacharComDuplicataEForaDeOrdemAsync(
            eventos,
            (evento, _, _) =>
            {
                chamadas.Add(evento.MessageId);
                return Task.CompletedTask;
            });

        Assert.Equal(sequenciaEsperada.Select(e => e.Evento.MessageId), chamadas);
    }

    private sealed record EventoDeTeste : EventoIntegracao;
}
