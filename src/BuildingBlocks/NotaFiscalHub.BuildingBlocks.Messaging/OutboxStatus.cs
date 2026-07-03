namespace NotaFiscalHub.BuildingBlocks.Messaging;

/// <summary>
/// Estados possíveis de uma linha da Outbox (spec B3 passo 2). Transições:
/// <c>Pendente</c> → <c>Processada</c> (todos os handlers executaram com sucesso);
/// <c>Pendente</c> → <c>SemHandler</c> (zero handlers registrados para o tipo — nunca <c>Processada</c>,
/// MSG0005 do legado);
/// <c>Pendente</c> → <c>Poison</c> (handler falhou <c>MaxTentativas</c> vezes, ou evento tenant-scoped
/// sem <c>ContaId</c>, ou tipo não desserializável).
/// <c>SemHandler</c> e <c>Poison</c> nunca transicionam automaticamente — exigem intervenção (handler
/// implementado + redisparo via runbook, ou supressão consciente).
/// </summary>
public enum OutboxStatus
{
    Pendente = 0,
    Processada = 1,
    SemHandler = 2,
    Poison = 3,
}
