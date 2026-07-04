using Microsoft.EntityFrameworkCore;
using NotaFiscalHub.BuildingBlocks.Persistence;

namespace NotaFiscalHub.BuildingBlocks.Auditoria;

/// <summary>
/// DbContext dedicado à tabela <c>auditoria.registro_auditoria</c>. Schema próprio do kernel — exceção
/// consciente à regra "schema pertence a módulo de negócio" (D-2026-07-01-10), mesma classe de exceção já
/// aplicada ao schema <c>kernel</c> de <c>IdempotencyDbContext</c> (Tarefa 4): a auditoria é cross-cutting
/// por natureza (design §2.2 "Cross-cutting kernel"), não pertence a nenhum módulo de negócio específico.
///
/// Estende <see cref="ModuleDbContext"/> DIRETAMENTE — NÃO <see cref="TenantDbContext"/> — porque
/// <see cref="RegistroAuditoriaEntity.ContaId"/> é <see cref="Nullable{Guid}"/> (registros de escopo de
/// sistema, spec B5 §Abordagem passo 6), o que é estruturalmente incompatível com
/// <see cref="TenantDbContext"/>: o filtro global compara <c>tenantContext.ContaId == e.ContaId</c> (dois
/// <see cref="Guid"/> não anuláveis) e <c>IsRequired()</c> na coluna mapeada, e
/// <see cref="TenantWriteInterceptor"/> faz um cast direto <c>(Guid)contaIdProperty.CurrentValue!</c> — os
/// dois quebrariam com um <see cref="Nullable{Guid}"/> real. Por isso <see cref="RegistroAuditoriaEntity"/>
/// também NÃO implementa <c>ITenantScopedEntity</c> (ver seu comentário de classe) e este DbContext não
/// recebe <c>ITenantContext</c> no construtor — não há filtro de tenant nem interceptor de escrita aqui.
///
/// O isolamento por tenant é responsabilidade EXCLUSIVA da camada de aplicação:
/// <c>ConsultaAuditoria</c> aplica <c>WHERE conta_id = @contaId</c> incondicionalmente (nunca retorna
/// linhas de outra conta nem de escopo de sistema); o handler que escreve
/// (<c>AuditoriaEventHandler</c>) grava <c>evento.ContaId</c> tal como veio do evento de integração, sem
/// nenhuma promoção/estampagem automática pelo contexto ambiente.
/// </summary>
public sealed class AuditoriaDbContext(DbContextOptions<AuditoriaDbContext> options) : ModuleDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AplicarAuditoria();
    }
}
