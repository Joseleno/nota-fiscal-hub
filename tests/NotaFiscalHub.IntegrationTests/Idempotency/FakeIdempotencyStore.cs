using System.Collections.Concurrent;
using NotaFiscalHub.BuildingBlocks.Idempotency;

namespace NotaFiscalHub.IntegrationTests.Idempotency;

/// <summary>
/// Fake em memória de <see cref="IIdempotencyStore"/>, usado por <see cref="EndpointFilterPlumbingTests"/>
/// para provar o comportamento do <see cref="IdempotencyEndpointFilter"/> (captura/replay de resposta,
/// não-dupla-execução de <see cref="IResult"/>) SEM depender de PostgreSQL real — a arbitração real da
/// corrida (unicidade de PK, UPDATE atômico de takeover) continua exclusiva dos testes com
/// <see cref="IdempotencyTestFixture"/> (Testcontainers). Reaplica a mesma máquina de decisão pura
/// (<see cref="IdempotencyDecisionRules"/>) usada pelo <see cref="IdempotencyStore"/> real, só que sobre um
/// dicionário em vez de uma tabela — por isso prova exatamente a integração filter↔decisão, não a
/// integração store↔banco.
/// </summary>
public sealed class FakeIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<(Guid, string, string, string), IdempotencyRecord> _registros = new();

    public Task<IdempotencyBeginResult> BeginAsync(IdempotencyRecord novo, CancellationToken ct)
    {
        var chave = (novo.ContaId, novo.Ambiente, novo.Rota, novo.Key);

        IdempotencyBeginResult resultado = default!;
        _registros.AddOrUpdate(
            chave,
            _ =>
            {
                resultado = new IdempotencyBeginResult(IdempotencyBeginOutcome.Inserted, null);
                return novo;
            },
            (_, existenteEntity) =>
            {
                var decisao = IdempotencyDecisionRules.Decidir(existenteEntity, novo.PayloadHashSha256, orfaoVencido: false);
                resultado = decisao;
                return decisao.Outcome == IdempotencyBeginOutcome.Inserted ? novo : existenteEntity;
            });

        return Task.FromResult(resultado);
    }

    public Task CompleteAsync(
        Guid contaId, string ambiente, string rota, string key,
        int status, string corpo, string contentType, string? location, CancellationToken ct)
    {
        var chave = (contaId, ambiente, rota, key);
        if (_registros.TryGetValue(chave, out var existente))
        {
            _registros[chave] = existente with
            {
                Estado = IdempotencyState.Concluida,
                RespostaStatus = status,
                RespostaCorpo = corpo,
                RespostaContentType = contentType,
                RespostaLocation = location,
            };
        }

        return Task.CompletedTask;
    }

    public Task ReleaseAsync(Guid contaId, string ambiente, string rota, string key, CancellationToken ct)
    {
        _registros.TryRemove((contaId, ambiente, rota, key), out _);
        return Task.CompletedTask;
    }
}
