using Microsoft.EntityFrameworkCore;
using NotaFiscalHub.BuildingBlocks.Auditoria.Contracts;

namespace NotaFiscalHub.BuildingBlocks.Auditoria;

/// <summary>
/// Implementação de <see cref="IConsultaAuditoria"/> (spec B5 §Abordagem passo 6). Como
/// <see cref="AuditoriaDbContext"/> NÃO tem filtro global de tenant (ver seu comentário de classe), o
/// <c>Where(r =&gt; r.ContaId == filtro.ContaId)</c> abaixo é a ÚNICA barreira de isolamento desta consulta —
/// incondicional, sempre aplicado, nunca contornável por parâmetro do chamador. Isso também exclui
/// automaticamente registros de escopo de sistema (<c>ContaId == null</c>): <c>null == filtro.ContaId</c>
/// (um <see cref="Guid"/> não anulável) nunca é verdadeiro.
/// </summary>
public sealed class ConsultaAuditoria(AuditoriaDbContext db) : IConsultaAuditoria
{
    public async Task<PaginaDe<RegistroAuditoria>> ConsultarAsync(FiltroAuditoria filtro, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filtro);

        var query = db.Set<RegistroAuditoriaEntity>()
            .AsNoTracking()
            .Where(r => r.ContaId == filtro.ContaId);

        if (filtro.De is { } de)
            query = query.Where(r => r.OcorridoEm >= de);

        if (filtro.Ate is { } ate)
            query = query.Where(r => r.OcorridoEm <= ate);

        if (!string.IsNullOrWhiteSpace(filtro.Acao))
            query = query.Where(r => r.Acao == filtro.Acao);

        if (!string.IsNullOrWhiteSpace(filtro.RecursoTipo))
            query = query.Where(r => r.RecursoTipo == filtro.RecursoTipo);

        var totalDeItens = await query.CountAsync(ct);

        var pagina = Math.Max(filtro.Pagina, 1);
        var tamanhoPagina = Math.Max(filtro.TamanhoPagina, 1);

        var entidades = await query
            .OrderByDescending(r => r.RegistradoEm)
            .Skip((pagina - 1) * tamanhoPagina)
            .Take(tamanhoPagina)
            .ToListAsync(ct);

        var itens = entidades.Select(Projetar).ToList();

        return new PaginaDe<RegistroAuditoria>(itens, totalDeItens, pagina, tamanhoPagina);
    }

    private static RegistroAuditoria Projetar(RegistroAuditoriaEntity e) => new(
        Id: e.Id,
        ContaId: e.ContaId,
        TipoEvento: e.TipoEvento,
        Acao: e.Acao,
        RecursoTipo: e.RecursoTipo,
        RecursoId: e.RecursoId,
        Ator: e.Ator,
        OcorridoEm: e.OcorridoEm,
        RegistradoEm: e.RegistradoEm,
        CorrelationId: e.CorrelationId,
        MessageId: e.MessageId,
        DadosJson: e.Dados);
}
