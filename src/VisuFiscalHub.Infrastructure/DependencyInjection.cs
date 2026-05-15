using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Domain.Interfaces;
using VisuFiscalHub.Infrastructure.Fiscal;
using VisuFiscalHub.Infrastructure.Fiscal.Certificates;
using VisuFiscalHub.Infrastructure.Fiscal.XmlBuilder;
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

        services.AddScoped<ITokenService, TokenService>();

        // Fase 6a — Fiscal infrastructure (real implementations)
        services.AddScoped<ICertificateEncryptionService, CertificateEncryptionService>();
        services.AddScoped<IQrCodeGenerator, QrCodeGenerator>();
        services.AddScoped<ITributacaoCalculator, TributacaoCalculator>();
        services.AddScoped<INfceXmlBuilder, NfceXmlBuilder>();
        services.AddSingleton<XmlSigner>();

        // Stubs — substituir por implementações reais em fases futuras
        services.AddScoped<ITenantCertificateProvider, TenantCertificateProviderStub>();
        services.AddScoped<ISefazClient, SefazClientStub>();
        services.AddScoped<IWebhookDeliveryService, WebhookDeliveryServiceStub>();
        services.AddScoped<IDocumentJobQueue, DocumentJobQueueStub>();

        return services;
    }
}
