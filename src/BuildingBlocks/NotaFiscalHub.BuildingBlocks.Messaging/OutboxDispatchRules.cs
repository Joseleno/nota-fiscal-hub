namespace NotaFiscalHub.BuildingBlocks.Messaging;

/// <summary>
/// Regras de decisão do <see cref="OutboxDispatcher{TDbContext}"/> extraídas como funções puras (sem
/// I/O) — spec B3 passos 4/5/6. Isoladas do acesso a dados para serem testáveis por unidade sem depender
/// de um PostgreSQL real (que só é necessário para a semântica transacional/de lock em si, coberta pelos
/// testes de integração em <c>tests/NotaFiscalHub.IntegrationTests/Messaging</c>).
/// </summary>
public static class OutboxDispatchRules
{
    /// <summary>
    /// Classifica o que o dispatcher deve fazer com uma mensagem antes de invocar qualquer handler
    /// (spec B3 passo 4, regra de escopo de tenant + passo 5, evento órfão).
    /// </summary>
    public static DecisaoDeDispatch Classificar(Guid? contaId, bool ehEventoDePlataforma, int totalDeHandlers)
    {
        if (contaId is null && !ehEventoDePlataforma)
            return DecisaoDeDispatch.PoisonImediato("evento tenant-scoped sem conta_id");

        if (contaId is not null && ehEventoDePlataforma)
            return DecisaoDeDispatch.PoisonImediato("evento de plataforma com conta_id preenchido (registro inconsistente)");

        if (totalDeHandlers == 0)
            return DecisaoDeDispatch.SemHandler();

        return contaId is { } valor && !ehEventoDePlataforma
            ? DecisaoDeDispatch.ExecutarComEscopoDeTenant(valor)
            : DecisaoDeDispatch.ExecutarSemEscopoDeTenant();
    }

    /// <summary>
    /// Defesa em profundidade na publicação (mesmo espírito do <c>TenantWriteInterceptor</c>): se há um
    /// escopo de tenant de NEGÓCIO ativo (não sistema) e o evento publicado traz um <c>ContaId</c>
    /// explícito diferente do tenant ambiente, é quase certamente um bug do chamador — deve falhar alto e
    /// cedo em vez de gravar silenciosamente um evento cruzado entre tenants. Eventos de plataforma
    /// (<c>ContaId</c> do evento nulo) e chamadas em escopo de sistema nunca são bloqueados aqui — essa
    /// decisão cabe à regra de dispatch (<see cref="Classificar"/>), não à publicação.
    /// </summary>
    public static bool ContaIdDoEventoDivergeDoTenantAmbiente(
        Guid? contaIdDoEvento, bool tenantAmbienteAtivo, bool ehEscopoDeSistema, Guid contaIdAmbiente)
    {
        if (contaIdDoEvento is not { } valor) return false;
        if (!tenantAmbienteAtivo || ehEscopoDeSistema) return false;

        return valor != contaIdAmbiente;
    }

    /// <summary>
    /// Decide a transição de estado após falha de handler (spec B3 passo 6): abaixo de
    /// <paramref name="maxTentativas"/>, volta a <c>Pendente</c> com backoff; ao atingir o limite, vira
    /// <c>Poison</c>. <paramref name="tentativasAnteriores"/> é o contador ANTES desta falha.
    /// </summary>
    public static DecisaoDeFalha ClassificarFalha(int tentativasAnteriores, int maxTentativas, int seedDeJitter)
    {
        var novaTentativa = tentativasAnteriores + 1;

        if (novaTentativa >= maxTentativas)
            return new DecisaoDeFalha(EhPoison: true, NovaTentativa: novaTentativa, Backoff: null);

        var backoff = BackoffCalculator.Calcular(novaTentativa, seedDeJitter);
        return new DecisaoDeFalha(EhPoison: false, NovaTentativa: novaTentativa, Backoff: backoff);
    }
}

public enum TipoDeDecisaoDeDispatch
{
    PoisonImediato,
    SemHandler,
    Executar,
}

public sealed record DecisaoDeDispatch(TipoDeDecisaoDeDispatch Tipo, string? MotivoDoPoison, Guid? ContaIdParaEscopo)
{
    public static DecisaoDeDispatch PoisonImediato(string motivo) =>
        new(TipoDeDecisaoDeDispatch.PoisonImediato, motivo, null);

    public static DecisaoDeDispatch SemHandler() =>
        new(TipoDeDecisaoDeDispatch.SemHandler, null, null);

    public static DecisaoDeDispatch ExecutarComEscopoDeTenant(Guid contaId) =>
        new(TipoDeDecisaoDeDispatch.Executar, null, contaId);

    public static DecisaoDeDispatch ExecutarSemEscopoDeTenant() =>
        new(TipoDeDecisaoDeDispatch.Executar, null, null);
}

public sealed record DecisaoDeFalha(bool EhPoison, int NovaTentativa, TimeSpan? Backoff);
