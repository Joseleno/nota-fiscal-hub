using NotaFiscalHub.BuildingBlocks.Messaging.Abstractions;

namespace NotaFiscalHub.BuildingBlocks.Messaging.TestKit;

/// <summary>
/// Test-kit para os módulos das Fases 1-4 (spec B3 passo 9 / plano de testes item 3): despacha uma
/// sequência de eventos em memória (sem banco, sem DI, sem dispatcher real) aplicando DUPLICAÇÃO e
/// EMBARALHAMENTO determinísticos — a mesma <paramref name="seed"/> sempre produz a mesma sequência de
/// entregas, tornando o teste reprodutível mesmo simulando at-least-once/fora-de-ordem.
///
/// Existe porque a biblioteca NÃO garante ordem de entrega por contrato (spec B3 passo 7: "paralelismo do
/// lote + retry reordenam naturalmente") — todo <see cref="IInboxHandler{T}"/> de negócio precisa provar
/// que tolera duplicata e reordenação ANTES de ir para produção, e rodar isso contra Testcontainers a cada
/// módulo seria lento; o harness prova a mesma propriedade em memória, em milissegundos.
/// </summary>
public sealed class OutboxTestHarness
{
    private readonly Random _random;

    public OutboxTestHarness(int seed)
    {
        _random = new Random(seed);
    }

    /// <summary>
    /// Despacha <paramref name="eventos"/> para <paramref name="handler"/> com duplicação e
    /// embaralhamento determinísticos: cada evento é entregue entre 1 e <paramref name="maxDuplicatas"/>
    /// vezes (inclusive), e a sequência final de entregas é embaralhada — nunca preserva a ordem de
    /// publicação. <paramref name="handler"/> deve ser idempotente: o harness não faz dedupe (isso é
    /// responsabilidade da Inbox real, já coberta pelos testes de integração da biblioteca) — o objetivo
    /// aqui é justamente PROVAR que o handler do módulo se comporta corretamente mesmo sem esse dedupe.
    /// </summary>
    public async Task DespacharComDuplicataEForaDeOrdemAsync<T>(
        IReadOnlyList<T> eventos, Func<T, MensagemContexto, CancellationToken, Task> handler,
        int maxDuplicatas = 3, CancellationToken ct = default)
        where T : EventoIntegracao
    {
        ArgumentNullException.ThrowIfNull(eventos);
        ArgumentNullException.ThrowIfNull(handler);
        if (maxDuplicatas < 1)
            throw new ArgumentOutOfRangeException(nameof(maxDuplicatas), maxDuplicatas, "Deve ser >= 1.");

        var entregas = MontarSequenciaDeEntregas(eventos, maxDuplicatas);

        foreach (var (evento, tentativa) in entregas)
        {
            var contexto = new MensagemContexto(evento.MessageId, CorrelationIdDeTeste(evento.MessageId), tentativa);
            await handler(evento, contexto, ct);
        }
    }

    /// <summary>
    /// Monta a sequência de entregas (evento, número da tentativa/duplicata) já duplicada e embaralhada —
    /// exposto separadamente de <see cref="DespacharComDuplicataEForaDeOrdemAsync{T}"/> para permitir que
    /// um teste inspecione a sequência planejada antes de executá-la (ex.: para asserções sobre "quantas
    /// duplicatas" sem depender de efeito colateral do handler).
    /// </summary>
    public IReadOnlyList<(T Evento, int Tentativa)> MontarSequenciaDeEntregas<T>(IReadOnlyList<T> eventos, int maxDuplicatas = 3)
        where T : EventoIntegracao
    {
        var entregas = new List<(T Evento, int Tentativa)>();

        foreach (var evento in eventos)
        {
            var vezes = _random.Next(1, maxDuplicatas + 1);
            for (var tentativa = 1; tentativa <= vezes; tentativa++)
            {
                entregas.Add((evento, tentativa));
            }
        }

        return Embaralhar(entregas);
    }

    /// <summary>Fisher-Yates com o <see cref="Random"/> seedado do harness — embaralhamento determinístico.</summary>
    private List<T> Embaralhar<T>(List<T> lista)
    {
        for (var i = lista.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (lista[i], lista[j]) = (lista[j], lista[i]);
        }

        return lista;
    }

    private static string CorrelationIdDeTeste(Guid messageId) => $"teste-{messageId:N}";
}
