namespace NotaFiscalHub.BuildingBlocks.Auditoria.Contracts;

/// <summary>
/// Consulta paginada e tenant-scoped da trilha de auditoria (spec B5 §Abordagem passo 6). Implementação
/// (<c>ConsultaAuditoria</c>) aplica <c>WHERE conta_id = @contaId</c> incondicionalmente — é a ÚNICA
/// barreira de isolamento de tenant desta fundação (não há filtro global de EF Core aqui, ver comentário
/// de classe de <c>AuditoriaDbContext</c>), então nunca retorna linhas de outra conta nem de escopo de
/// sistema (<c>conta_id NULL</c>).
/// </summary>
public interface IConsultaAuditoria
{
    Task<PaginaDe<RegistroAuditoria>> ConsultarAsync(FiltroAuditoria filtro, CancellationToken ct);
}
