namespace NotaFiscalHub.BuildingBlocks.Messaging;

/// <summary>
/// Linha da tabela <c>outbox</c> do schema do módulo produtor (spec B3 passo 2). <see cref="Id"/> é o
/// <c>MessageId</c> do evento — a mesma identidade usada pela Inbox do consumidor para dedupe.
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>Id = MessageId do <c>EventoIntegracao</c> publicado.</summary>
    public Guid Id { get; set; }

    /// <summary>Nome lógico versionado do tipo de evento (ex.: <c>empresas.empresa_criada.v1</c>).</summary>
    public string TipoEvento { get; set; } = string.Empty;

    /// <summary>Payload serializado (jsonb) do evento — só identificadores/status (design §2.3.3/§2.3.6).</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary><see langword="null"/> só para eventos de plataforma (<c>AddEventoDePlataforma&lt;T&gt;()</c>).</summary>
    public Guid? ContaId { get; set; }

    public string CorrelationId { get; set; } = string.Empty;

    public DateTimeOffset OcorridoEm { get; set; }

    public OutboxStatus Status { get; set; } = OutboxStatus.Pendente;

    public int Tentativas { get; set; }

    public DateTimeOffset ProximaTentativaEm { get; set; }

    public DateTimeOffset? ProcessadaEm { get; set; }

    public string? ErroUltimo { get; set; }
}
