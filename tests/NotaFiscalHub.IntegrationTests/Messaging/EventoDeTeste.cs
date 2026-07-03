using NotaFiscalHub.BuildingBlocks.Messaging.Abstractions;

namespace NotaFiscalHub.IntegrationTests.Messaging;

/// <summary>Evento de integração mínimo usado pelos testes de integração da biblioteca Outbox/Inbox.</summary>
public sealed record EventoDeTeste : EventoIntegracao
{
    public string Rotulo { get; init; } = string.Empty;
}

/// <summary>Segundo tipo de evento — usado quando um teste precisa de dois tipos distintos na mesma fila.</summary>
public sealed record OutroEventoDeTeste : EventoIntegracao
{
    public string Rotulo { get; init; } = string.Empty;
}

/// <summary>Evento sem NENHUM handler registrado — prova a política de órfão (spec B3 passo 5).</summary>
public sealed record EventoSemHandlerDeTeste : EventoIntegracao;

/// <summary>Evento de plataforma (whitelist <c>AddEventoDePlataforma</c>) — nunca tem <c>ContaId</c>.</summary>
public sealed record EventoDePlataformaDeTeste : EventoIntegracao;

/// <summary>Contador simples e thread-safe usado para provar quantas vezes um handler executou de fato.</summary>
public sealed class ContadorDeExecucoes
{
    private int _total;
    public int Total => _total;
    public void Incrementar() => Interlocked.Increment(ref _total);
}
