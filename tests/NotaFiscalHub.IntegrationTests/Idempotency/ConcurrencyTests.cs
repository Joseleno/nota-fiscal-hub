using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using NotaFiscalHub.Api.Testing;

namespace NotaFiscalHub.IntegrationTests.Idempotency;

/// <summary>
/// Critério de aceite 4 (spec B4): N≥8 requisições paralelas com a mesma key + mesmo corpo — exatamente 1
/// executa o handler; as demais recebem replay ou <c>409 requisicao_em_processamento</c> com
/// <c>Retry-After</c>. A corrida é arbitrada pela PK ÚNICA do INSERT no banco (<see cref="IdempotencyStore"/>),
/// nunca por lock em processo — só um Postgres real sob <c>Task.WhenAll</c> real prova isso; nenhum double
/// em memória reproduz a semântica de violação de unicidade sob concorrência real.
///
/// NÃO EXECUTA neste ambiente: requer PostgreSQL real via Testcontainers/Docker, indisponível aqui (ver
/// task-4-report.md, "Gap de ambiente"). Escrito e revisado para rodar em CI/ambiente com Docker — é o
/// teste mais crítico da tarefa (prova a arbitração da corrida) e o que mais precisa rodar em CI antes do
/// merge/deploy real.
/// </summary>
public class ConcurrencyTests : IClassFixture<IdempotencyTestFixture>
{
    private readonly IdempotencyTestFixture _fixture;

    public ConcurrencyTests(IdempotencyTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task NRequisicoesParalelas_MesmaKey_ExatamenteUmaExecutaOHandler()
    {
        using var factory = _fixture.CriarFactory();
        var contador = factory.Services.GetRequiredService<ContadorDoHandlerFake>();
        contador.Resetar();

        var contaId = Guid.NewGuid();

        var tarefas = Enumerable.Range(0, 8).Select(_ =>
        {
            using var cliente = IdempotencyTestFixture.ClienteComTenant(factory, contaId);
            cliente.DefaultRequestHeaders.Add("Idempotency-Key", "y");
            return cliente.PostAsync("/v1/nfce", JsonContent.Create(new { valor = 1 }));
        });

        var respostas = await Task.WhenAll(tarefas);

        Assert.Equal(1, contador.Total);

        // Cada resposta é 201 (a original ou replay dela) ou 409 requisicao_em_processamento — nunca outro
        // status (ex.: 500 indicaria uma falha não tratada na arbitração da corrida).
        Assert.All(respostas, r => Assert.True(
            r.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict,
            $"Status inesperado: {r.StatusCode}"));
    }

    [Fact]
    public async Task NRequisicoesParalelas_Perdedoras_RecebemRetryAfter()
    {
        using var factory = _fixture.CriarFactory();
        var contador = factory.Services.GetRequiredService<ContadorDoHandlerFake>();
        contador.Resetar();

        var contaId = Guid.NewGuid();

        var tarefas = Enumerable.Range(0, 8).Select(_ =>
        {
            using var cliente = IdempotencyTestFixture.ClienteComTenant(factory, contaId);
            cliente.DefaultRequestHeaders.Add("Idempotency-Key", "y2");
            return cliente.PostAsync("/v1/nfce", JsonContent.Create(new { valor = 1 }));
        });

        var respostas = await Task.WhenAll(tarefas);

        var perdedoras = respostas.Where(r => r.StatusCode == HttpStatusCode.Conflict).ToList();
        Assert.All(perdedoras, r => Assert.True(r.Headers.RetryAfter is not null));
    }
}
