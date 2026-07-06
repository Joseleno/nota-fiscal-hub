namespace NotaFiscalHub.BuildingBlocks.Observability;

/// <summary>
/// Implementação ambiente de <see cref="ICorrelationContext"/> baseada em <see cref="AsyncLocal{T}"/> —
/// mesmo desenho de <c>AmbientTenantContext</c> (Tarefa 2): o valor flui automaticamente por
/// <c>async</c>/<c>await</c> e é isolado por fluxo de execução lógico (duas requisições concorrentes, ou
/// dois ciclos de dispatch do outbox rodando em paralelo, nunca compartilham o mesmo valor).
///
/// Registrado como singleton nos hosts (mesma justificativa de <c>AmbientTenantContext</c>): o estado
/// "por fluxo" vive no <see cref="AsyncLocal{T}"/>, não na instância do serviço.
/// </summary>
public sealed class CorrelationContext : ICorrelationContext
{
    private readonly AsyncLocal<string?> _correlationIdAtual = new();

    public string CorrelationId => _correlationIdAtual.Value
        ?? throw new InvalidOperationException(
            $"{nameof(ICorrelationContext)}.{nameof(CorrelationId)} lido antes de {nameof(Definir)} ser " +
            "chamado no fluxo corrente. Toda borda de entrada (middleware HTTP, ciclo de dispatch do " +
            "outbox, job do Worker) precisa abrir um escopo de correlação antes de rodar código de " +
            "aplicação — nunca retornar vazio/nulo silenciosamente.");

    /// <summary>
    /// Define o CorrelationId do fluxo corrente e devolve um <see cref="IDisposable"/> que restaura o
    /// valor anterior (tipicamente <see langword="null"/>) ao sair do escopo — mesmo padrão de
    /// empilhamento do <c>AmbientTenantContext</c>, necessário para o dispatcher da Outbox: cada
    /// mensagem processada abre e fecha seu próprio escopo, nunca herdando o CorrelationId de uma
    /// mensagem processada anteriormente no mesmo ciclo/thread.
    /// </summary>
    public IDisposable Definir(string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        var anterior = _correlationIdAtual.Value;
        _correlationIdAtual.Value = correlationId;
        return new EscopoDeCorrelacao(() => _correlationIdAtual.Value = anterior);
    }

    private sealed class EscopoDeCorrelacao(Action aoDispor) : IDisposable
    {
        private bool _disposto;

        public void Dispose()
        {
            if (_disposto) return;
            _disposto = true;
            aoDispor();
        }
    }
}
