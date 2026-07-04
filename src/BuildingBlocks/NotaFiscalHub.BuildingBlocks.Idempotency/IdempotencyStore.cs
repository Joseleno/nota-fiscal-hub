using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace NotaFiscalHub.BuildingBlocks.Idempotency;

/// <summary>
/// Implementação de <see cref="IIdempotencyStore"/> contra <see cref="IdempotencyDbContext"/> (spec B4
/// §Abordagem passos 1/3/5). A corrida entre requisições concorrentes com a mesma
/// <c>(ContaId, Ambiente, Rota, Key)</c> é sempre arbitrada pelo PRÓPRIO BANCO — nunca por lock em
/// processo/verificação-então-ação em memória:
///
/// 1. <see cref="BeginAsync"/> tenta um <c>INSERT</c> otimista primeiro. A PK composta é a fonte de
///    verdade: se duas requisições tentam inserir a mesma PK ao mesmo tempo, o banco garante que só uma
///    committa — a outra recebe uma violação de unicidade (23505), nunca as duas "veem" sucesso.
/// 2. Ao encontrar a PK já ocupada, relê o registro existente. Se estiver <c>EmProcessamento</c> e mais
///    velho que <see cref="IdempotencyOptions.OrphanTimeout"/>, tenta o takeover atômico
///    (<see cref="IdempotencySql.TakeoverOrfao"/>) — um único <c>UPDATE ... WHERE estado = 'EmProcessamento'
///    AND criada_em &lt; now() - @orphanTimeout</c>: 1 linha afetada = esta chamada venceu (mesmo
///    fundamento do <c>UPDATE ... FOR UPDATE SKIP LOCKED ... RETURNING</c> da Outbox, Tarefa 3— a decisão
///    e a "posse" são gravadas no MESMO statement que testa a condição, sem janela entre leitura e ação).
/// 3. Só depois de esgotadas as duas tentativas de vencer a corrida, a decisão final
///    (replay/conflito/corrida-em-andamento) é delegada à função pura <see cref="IdempotencyDecisionRules.Decidir"/>.
/// </summary>
public sealed class IdempotencyStore(
    IdempotencyDbContext db, TimeProvider relogio, IOptions<IdempotencyOptions> opcoes) : IIdempotencyStore
{
    private readonly IdempotencyOptions _opcoes = opcoes.Value;

    public async Task<IdempotencyBeginResult> BeginAsync(IdempotencyRecord novo, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();

        var entidade = new IdempotencyRecordEntity
        {
            ContaId = novo.ContaId,
            Ambiente = novo.Ambiente,
            Rota = novo.Rota,
            Key = novo.Key,
            PayloadHashSha256 = novo.PayloadHashSha256,
            Estado = IdempotencyState.EmProcessamento,
            CriadaEm = agora,
            ExpiraEm = agora + _opcoes.Ttl,
        };

        db.Set<IdempotencyRecordEntity>().Add(entidade);

        try
        {
            await db.SaveChangesAsync(ct);
            return new IdempotencyBeginResult(IdempotencyBeginOutcome.Inserted, null);
        }
        catch (DbUpdateException ex) when (EhViolacaoDeChavePrimaria(ex))
        {
            db.Entry(entidade).State = EntityState.Detached;
        }

        // A PK já está ocupada — outra requisição venceu o INSERT. Relê o estado atual para decidir.
        var existente = await LerExistenteAsync(novo.ContaId, novo.Ambiente, novo.Rota, novo.Key, ct);

        if (existente is { Estado: IdempotencyState.EmProcessamento } &&
            EstaOrfaoVencido(existente.CriadaEm, agora))
        {
            var linhasAfetadas = await TentarTakeoverAsync(novo, agora, ct);
            if (linhasAfetadas == 1)
                return new IdempotencyBeginResult(IdempotencyBeginOutcome.Inserted, null);

            // Perdeu o takeover para outra requisição concorrente — relê de novo e cai no fluxo normal.
            existente = await LerExistenteAsync(novo.ContaId, novo.Ambiente, novo.Rota, novo.Key, ct);
        }

        var orfaoVencido = existente is { Estado: IdempotencyState.EmProcessamento } &&
                            EstaOrfaoVencido(existente.CriadaEm, agora);

        return IdempotencyDecisionRules.Decidir(existente, novo.PayloadHashSha256, orfaoVencido);
    }

    public async Task CompleteAsync(
        Guid contaId, string ambiente, string rota, string key,
        int status, string corpo, string contentType, string? location, CancellationToken ct)
    {
        var corpoArmazenado = corpo.Length > _opcoes.TamanhoMaximoDoCorpoArmazenado
            ? corpo[.._opcoes.TamanhoMaximoDoCorpoArmazenado]
            : corpo;

        await db.Set<IdempotencyRecordEntity>()
            .Where(r => r.ContaId == contaId && r.Ambiente == ambiente && r.Rota == rota && r.Key == key)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Estado, IdempotencyState.Concluida)
                .SetProperty(r => r.RespostaStatus, status)
                .SetProperty(r => r.RespostaCorpo, corpoArmazenado)
                .SetProperty(r => r.RespostaContentType, contentType)
                .SetProperty(r => r.RespostaLocation, location), ct);
    }

    public async Task ReleaseAsync(Guid contaId, string ambiente, string rota, string key, CancellationToken ct)
    {
        await db.Set<IdempotencyRecordEntity>()
            .Where(r => r.ContaId == contaId && r.Ambiente == ambiente && r.Rota == rota && r.Key == key
                        && r.Estado == IdempotencyState.EmProcessamento)
            .ExecuteDeleteAsync(ct);
    }

    private bool EstaOrfaoVencido(DateTimeOffset criadaEm, DateTimeOffset agora) =>
        agora - criadaEm >= _opcoes.OrphanTimeout;

    private async Task<int> TentarTakeoverAsync(IdempotencyRecord novo, DateTimeOffset agora, CancellationToken ct)
    {
        var schema = db.Model.FindEntityType(typeof(IdempotencyRecordEntity))!.GetSchema()!;
        var sql = IdempotencySql.TakeoverOrfao(schema, _opcoes.OrphanTimeout);

        return await db.Database.ExecuteSqlRawAsync(
            sql, [novo.PayloadHashSha256, novo.ContaId, novo.Ambiente, novo.Rota, novo.Key], ct);
    }

    private async Task<IdempotencyRecord?> LerExistenteAsync(
        Guid contaId, string ambiente, string rota, string key, CancellationToken ct)
    {
        var entidade = await db.Set<IdempotencyRecordEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ContaId == contaId && r.Ambiente == ambiente && r.Rota == rota && r.Key == key, ct);

        return entidade is null ? null : ParaRecord(entidade);
    }

    private static IdempotencyRecord ParaRecord(IdempotencyRecordEntity e) => new(
        e.ContaId, e.Ambiente, e.Key, e.Rota, e.PayloadHashSha256, e.Estado,
        e.RespostaStatus, e.RespostaCorpo, e.RespostaContentType, e.RespostaLocation, e.CriadaEm, e.ExpiraEm);

    /// <summary>
    /// SqlState 23505 (unique_violation) do Postgres — via Npgsql, a exceção real vem embrulhada em
    /// <see cref="DbUpdateException.InnerException"/>. Verifica pelo nome do tipo (em vez de referenciar
    /// o pacote Npgsql diretamente) para não acoplar este projeto ao provider de banco — mesmo espírito de
    /// manter <see cref="IdempotencySql"/> como texto puro, sem dependência de driver.
    /// </summary>
    private static bool EhViolacaoDeChavePrimaria(DbUpdateException ex) =>
        ex.InnerException is { } inner &&
        inner.GetType().Name == "PostgresException" &&
        inner.GetType().GetProperty("SqlState")?.GetValue(inner) as string == "23505";
}
