using HealthChecks.NpgSql;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

namespace NotaFiscalHub.BuildingBlocks.Observability;

/// <summary>
/// Bootstrap único de observabilidade (spec B7 passo 1/6) consumido pelos 2 hosts (API/Worker) — a MESMA
/// configuração de Serilog/OTel/health em vez de copiar/colar entre eles. Ver
/// <c>BuildingBlocks/Observability/README.md</c> para a visão geral e como estender.
/// </summary>
public static class ObservabilityExtensions
{
    /// <summary>Chave de configuração do endpoint OTLP — vazio/ausente = nenhum exporter (spec B7 critério 6).</summary>
    public const string ChaveConfigOtlpEndpoint = "Observability:Otlp:Endpoint";

    /// <summary>Chave de configuração da connection string usada só para o health check de Postgres.</summary>
    public const string ChaveConfigPostgresHealthCheck = "Observability:Postgres:ConnectionString";

    private const string NomeDoHealthCheckPostgres = "postgres";

    /// <summary>
    /// Registra Serilog (console JSON + <see cref="PiiMaskingEnricher"/>), <see cref="ICorrelationContext"/>,
    /// OpenTelemetry (traces ASP.NET Core + HttpClient + EF Core, resource com <paramref name="serviceName"/>,
    /// exporter OTLP condicional à configuração) e o health check base de PostgreSQL.
    /// </summary>
    public static IHostApplicationBuilder AddNfhObservability(this IHostApplicationBuilder builder, string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        builder.Services.AddSingleton<CorrelationContext>();
        builder.Services.AddSingleton<ICorrelationContext>(sp => sp.GetRequiredService<CorrelationContext>());

        builder.Services.AddSerilog((servicos, loggerConfiguration) =>
        {
            loggerConfiguration
                .ReadFrom.Configuration(builder.Configuration)
                .Enrich.FromLogContext()
                .Enrich.With<PiiMaskingEnricher>()
                .Enrich.WithProperty("service.name", serviceName)
                .WriteTo.Console(new Serilog.Formatting.Compact.CompactJsonFormatter());
        });

        var otlpEndpoint = builder.Configuration[ChaveConfigOtlpEndpoint];

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: serviceName,
                serviceVersion: typeof(ObservabilityExtensions).Assembly.GetName().Version?.ToString()))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(serviceName)
                    .AddAspNetCoreInstrumentation(o =>
                    {
                        // Ruído (spec B7 passo 4): /alive e /health são chamados por probes de
                        // orquestração em alta frequência e não agregam valor de observabilidade.
                        o.Filter = context =>
                            !context.Request.Path.StartsWithSegments("/alive") &&
                            !context.Request.Path.StartsWithSegments("/health");
                    })
                    .AddHttpClientInstrumentation()
                    // Pacote OpenTelemetry.Instrumentation.EntityFrameworkCore (1.16.0-beta.1): por padrão
                    // já emite o TEXTO do statement (com placeholders de parâmetro, ex. @p0 — equivalente
                    // ao "SetDbStatementForText=true" de outras instrumentações OTel) SEM os VALORES dos
                    // parâmetros. Valor de parâmetro só seria incluído com a env var experimental
                    // OTEL_DOTNET_EXPERIMENTAL_EFCORE_ENABLE_TRACE_DB_QUERY_PARAMETERS — nunca setada aqui,
                    // deliberadamente (parâmetro pode conter CPF/CNPJ/XML — mesma política anti-PII vale
                    // para traces, spec B7 "Riscos"). Não há callback público para reforçar isso além de
                    // simplesmente nunca setar a env var; T5 verifica o resultado (nenhum valor de CPF na
                    // tag do span).
                    .AddEntityFrameworkCoreInstrumentation()
                    // AddNpgsql() (pacote Npgsql.OpenTelemetry): ActivitySource nativo do driver, nomeado
                    // "Npgsql" — span de nível mais baixo que o da instrumentação EF Core acima (comando SQL
                    // real enviado à conexão). Mantido junto com AddEntityFrameworkCoreInstrumentation
                    // (spec B7 T5 verifica especificamente o span cuja Source.Name contém "Npgsql").
                    .AddNpgsql();

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
                }
            });

        var postgresConnectionString = builder.Configuration[ChaveConfigPostgresHealthCheck];
        var healthChecksBuilder = builder.Services.AddHealthChecks();

        if (!string.IsNullOrWhiteSpace(postgresConnectionString))
        {
            healthChecksBuilder.AddNpgSql(
                postgresConnectionString,
                name: NomeDoHealthCheckPostgres,
                tags: ["ready"]);
        }

        return builder;
    }

    /// <summary>
    /// Middleware de CorrelationId + endpoints <c>/alive</c> (liveness — processo apenas, nunca falha por
    /// dependência externa) e <c>/health</c> (readiness — inclui o check de Postgres, spec B7 passo 5).
    /// </summary>
    public static WebApplication UseNfhObservability(this WebApplication app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();

        app.MapHealthChecks("/alive", new HealthCheckOptions
        {
            Predicate = _ => false,
        });

        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
        });

        return app;
    }
}
