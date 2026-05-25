namespace VisuFiscalHub.Domain.Enums;

public enum TipoTentativa
{
    Envio = 1,
    Consulta = 2,
    // 3 removido (era Retry — nunca persistido no banco)
    Cancelamento = 4   // tentativa de cancelamento via NfeRecepcaoEvento4
}
