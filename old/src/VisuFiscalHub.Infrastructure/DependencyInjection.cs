using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Interfaces;
using VisuFiscalHub.Infrastructure.Fiscal;
using VisuFiscalHub.Infrastructure.Fiscal.Certificates;
using VisuFiscalHub.Infrastructure.Fiscal.Prefeitura;
using VisuFiscalHub.Infrastructure.Fiscal.Sefaz;
using VisuFiscalHub.Infrastructure.Fiscal.XmlBuilder;
using VisuFiscalHub.Infrastructure.Jobs;
using VisuFiscalHub.Infrastructure.Persistence;
using VisuFiscalHub.Infrastructure.Persistence.Repositories;
using VisuFiscalHub.Infrastructure.Services;
using VisuFiscalHub.Infrastructure.Services.Stubs;
using VisuFiscalHub.Infrastructure.Fiscal.Stubs;

namespace VisuFiscalHub.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment? environment = null)
    {
        // Singleton explícito — sem dependências scoped; evita scope leak silencioso se dependências forem adicionadas
        services.AddSingleton<DomainEventsInterceptor>();

        bool isTest = string.Equals(environment?.EnvironmentName, "Test", StringComparison.OrdinalIgnoreCase);

        var connectionString = isTest
            ? null
            : configuration.GetConnectionString("DefaultConnection")
              ?? throw new InvalidOperationException(
                  "Connection string 'DefaultConnection' não configurada.");

        if (isTest)
        {
            // Use a fixed name per service registration so all scopes within the same
            // WebApplicationFactory share the same in-memory store, while different factory
            // instances get isolated stores.
            var testDbName = $"TestDb_{Guid.NewGuid():N}";
            services.AddDbContext<ApplicationDbContext>(options =>
                options
                    .UseInMemoryDatabase(testDbName)
                    .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        }
        else
        {
            services.AddDbContext<ApplicationDbContext>((sp, options) =>
            {
                options.UseNpgsql(
                    connectionString!,
                    npgsql => npgsql.MigrationsAssembly(
                        typeof(ApplicationDbContext).Assembly.FullName));

                options.AddInterceptors(sp.GetRequiredService<DomainEventsInterceptor>());
            });
        }

        services.AddSingleton(TimeProvider.System);

        services.AddHttpContextAccessor();
        services.AddScoped<ICorrelationContext, HttpCorrelationContext>();

        services.AddScoped<IClienteAppRepository, ClienteAppRepository>();
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IDocumentoFiscalRepository, DocumentoFiscalRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ISequenceManager, SequenceManager>();

        services.AddSingleton<ITokenService, TokenService>();

        // Fase 6a — Fiscal infrastructure
        services.AddSingleton<ICertificateEncryptionService, CertificateEncryptionService>();
        services.AddScoped<IQrCodeGenerator, QrCodeGenerator>();
        services.AddScoped<ITributacaoCalculator, TributacaoCalculator>();
        services.AddScoped<IFiscalDocumentXmlBuilder, FiscalDocumentXmlBuilder>();
        services.AddSingleton<XmlSigner>();

        // Fase 6b — Certificados com cache
        services.AddMemoryCache();
        services.AddScoped<ITenantCertificateProvider, TenantCertificateProvider>();

        // Fase 6b — Webhook delivery
        // AllowAutoRedirect = false: previne bypass de SSRF via redirect (e.g., 301 → 169.254.x.x).
        services.AddHttpClient("webhook", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Add("User-Agent", "VisuFiscalHub-Webhook/1.0");
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false
        });
        services.AddScoped<IWebhookDeliveryService, WebhookDeliveryService>();

        // Fase 6b — Hangfire (job queue + outbox relay)
        // In Test environment, skip PostgreSQL-backed storage (no real DB available).
        services.AddHangfire(config =>
        {
            config
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UseFilter(new CorrelationIdJobFilter());
            if (!isTest)
                config.UsePostgreSqlStorage(opts => opts.UseNpgsqlConnection(connectionString));
        });

        if (!isTest)
            services.AddHangfireServer(opts =>
            {
                opts.WorkerCount = 4;
                opts.Queues = ["default"];
            });

        services.AddScoped<IDocumentJobQueue, DocumentJobQueue>();
        services.AddScoped<OutboxRelayJob>();
        services.AddScoped<FiscalDocumentProcessingJob>();
        services.AddScoped<ReconciliacaoJobProcessor>();
        services.AddScoped<CancelamentoJob>();
        services.AddScoped<ICancelamentoJobQueue, HangfireCancelamentoJobQueue>();

        // Fase 7 — Integração SEFAZ
        // SefazHttpClient é registrado sempre — CancelamentoJob injeta-o diretamente.
        // Sefaz:UseFakeClient=true substitui apenas ISefazClient por stub local.
        services.AddScoped<SefazHttpClient>();
        bool useFakeClient = configuration.GetValue<bool>("Sefaz:UseFakeClient");
        if (useFakeClient)
        {
            services.AddScoped<ISefazClient, FakeSefazClient>();
        }
        else
        {
            services.AddScoped<ISefazClient, SefazClient>();
        }

        // Fase 15 — NFS-e ABRASF
        services.AddScoped<INfseXmlBuilder, NfseXmlBuilder>();
        if (useFakeClient)
        {
            services.AddScoped<IPrefeituraClient, FakePrefeituraClient>();
        }
        else
        {
            services.AddScoped<PrefeituraHttpClient>();
            services.AddScoped<IPrefeituraClient, PrefeituraClient>();
        }
        services.AddScoped<PrefeituraProcessingJob>();

        return services;
    }
}
