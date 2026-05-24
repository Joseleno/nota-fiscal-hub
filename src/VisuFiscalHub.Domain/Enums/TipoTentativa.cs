namespace VisuFiscalHub.Domain.Enums;

public enum TipoTentativa
{
    Envio = 1,
    Consulta = 2,
    Retry = 3,
    Cancelamento = 4   // tentativa de cancelamento via NfeRecepcaoEvento4
}
