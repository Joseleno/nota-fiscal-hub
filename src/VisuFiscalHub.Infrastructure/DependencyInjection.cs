using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Interfaces;
using VisuFiscalHub.Infrastructure.Fiscal;
using VisuFiscalHub.Infrastructure.Fiscal.Certificates;
using VisuFiscalHub.Infrastructure.Fiscal.Sefaz;
using VisuFiscalHub.Infrastructure.Fiscal.XmlBuilder;
using VisuFiscalHub.Infrastructure.Jobs;
using VisuFiscalHub.Infrastructure.Persistence;
using VisuFiscalHub.Infrastructure.Persistence.Repositories;
using VisuFiscalHub.Infrastructure.Services;
using VisuFiscalHub.Infrastructure.Services.Stubs;

namespace VisuFiscalHub.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Singleton explícito — sem dependências scoped; evita scope leak silencioso se dependências forem adicionadas
        services.AddSingleton<DomainEventsInterceptor>();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Connection string 'DefaultConnection' não configurada.");

        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsAssembly(
                    typeof(ApplicationDbContext).Assembly.FullName));

            options.AddInterceptors(sp.GetRequiredService<DomainEventsInterceptor>());
        });

        services.AddSingleton(TimeProvider.System);

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
        services.AddScoped<INfceXmlBuilder, NfceXmlBuilder>();
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
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(opts => opts.UseNpgsqlConnection(connectionString)));

        services.AddHangfireServer(opts =>
        {
            opts.WorkerCount = 4;
            opts.Queues = ["default"];
        });

        services.AddScoped<IDocumentJobQueue, DocumentJobQueue>();
        services.AddScoped<OutboxRelayJob>();
        services.AddScoped<NfceProcessingJob>();
        services.AddScoped<ReconciliacaoJobProcessor>();

        // Fase 7 — Integração SEFAZ
        // SefazHttpClient cria HttpClient por request para mTLS por-tenant — não usa factory.
        // User-Agent hardcoded em SefazHttpClient.UserAgent (constante sincronizada).
        services.AddScoped<SefazHttpClient>();
        services.AddScoped<ISefazClient, SefazClient>();

        return services;
    }
}
