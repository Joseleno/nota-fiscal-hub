extern alias WorkerHost;

using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NotaFiscalHub.BuildingBlocks.Idempotency;
using Testcontainers.PostgreSql;
using WorkerProgram = WorkerHost::Program;

namespace NotaFiscalHub.IntegrationTests.Observability;

/// <summary>
/// T6 (spec B7) — health checks nos 2 deployables: <c>/alive</c> sempre 200 (processo apenas,
/// <c>Predicate = _ =&gt; false</c> não avalia dependências); <c>/health</c> 200 com Postgres no ar e 503
/// com Postgres derrubado (readiness real via <c>AddNpgSql</c>).
///
/// NÃO EXECUTA neste ambiente: requer PostgreSQL real via Testcontainers/Docker — em particular, este
/// teste precisa PARAR o container no meio do teste (<c>StopAsync</c>), o que é o cenário mais dependente
/// de Docker real de toda a Tarefa 7. Mesma lacuna documentada nas Tarefas 1-6.
///
/// Nos cenários contra o host Api, <see cref="IdempotencyExpirationJob"/> (Tarefa 4) é removido do host de
/// teste: esse job faz polling do Postgres SEM try/catch ao redor da chamada ao banco em seu loop
/// (<c>ExecuteAsync</c>) — com <c>BackgroundServiceExceptionBehavior</c> padrão (<c>StopHost</c>), uma
/// falha do Postgres nele derruba o host inteiro (efeito colateral de um gap PRÉ-EXISTENTE da Tarefa 4,
/// não desta tarefa). Removê-lo isola o teste no comportamento que é objeto da Tarefa 7 — o health check
/// em si — sem ficar refém de um bug de resiliência de outro componente que compartilha o mesmo host.
/// </summary>
public class HealthChecksTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    public async Task InitializeAsync() => await _postgres.StartAsync();
    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Api_Health_PostgresNoAr_Retorna200_Derrubado_Retorna503()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Idempotency", _postgres.GetConnectionString());
            builder.UseSetting("ConnectionStrings:Auditoria", _postgres.GetConnectionString());
            builder.ConfigureServices(RemoverHostedServicesQueNaoSaoObjetoDesteTeste);
        });
        using var cliente = factory.CreateClient();

        var respostaOk = await cliente.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, respostaOk.StatusCode);

        await _postgres.StopAsync();

        var respostaFalha = await cliente.GetAsync("/health");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, respostaFalha.StatusCode);
    }

    [Fact]
    public async Task Api_Alive_SempreRetorna200_MesmoComPostgresDerrubado()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Idempotency", _postgres.GetConnectionString());
            builder.UseSetting("ConnectionStrings:Auditoria", _postgres.GetConnectionString());
            builder.ConfigureServices(RemoverHostedServicesQueNaoSaoObjetoDesteTeste);
        });
        using var cliente = factory.CreateClient();

        await _postgres.StopAsync();

        var resposta = await cliente.GetAsync("/alive");
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    /// <summary>
    /// Remove TODOS os <see cref="IHostedService"/> do host de teste (incl. <see cref="IdempotencyExpirationJob"/>
    /// e os dispatchers/retention da Tarefa 3) — ver comentário de classe: o job de expiração não tem
    /// try/catch em torno da chamada ao Postgres no seu loop de polling, e com Postgres derrubado
    /// deliberadamente pelo teste, uma falha nele derrubaria o host inteiro (gap pré-existente da Tarefa 4)
    /// antes que o teste consiga observar o comportamento do health check em si — que é o único objeto
    /// desta suíte (Tarefa 7).
    /// </summary>
    private static void RemoverHostedServicesQueNaoSaoObjetoDesteTeste(IServiceCollection services) =>
        services.RemoveAll<IHostedService>();

    [Fact]
    public async Task Worker_Health_PostgresNoAr_Retorna200_Derrubado_Retorna503()
    {
        using var factory = new WebApplicationFactory<WorkerProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Idempotency", _postgres.GetConnectionString());
        });
        using var cliente = factory.CreateClient();

        var respostaOk = await cliente.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, respostaOk.StatusCode);

        await _postgres.StopAsync();

        var respostaFalha = await cliente.GetAsync("/health");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, respostaFalha.StatusCode);
    }

    [Fact]
    public async Task Worker_Alive_SempreRetorna200()
    {
        using var factory = new WebApplicationFactory<WorkerProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Idempotency", _postgres.GetConnectionString());
        });
        using var cliente = factory.CreateClient();

        var resposta = await cliente.GetAsync("/alive");
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }
}
