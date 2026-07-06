namespace NotaFiscalHub.Modules.Documentos.Application;

// VIOLACAO-ISCA (Tarefa 9 / prova RED do gate test-arquitetura, revertida antes do merge):
// referencia real (IL) de Documentos.Application para ContasPlanos.Contracts, violando §2.5.
public static class ViolacaoIscaT1
{
    public static readonly System.Type ReferenciaProibida = typeof(NotaFiscalHub.Modules.ContasPlanos.Contracts.AssemblyMarker);
}
