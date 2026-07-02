using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using VisuFiscalHub.Application.Common.Interfaces;

namespace VisuFiscalHub.Tests.Integration.Infrastructure;

public sealed class VisuFiscalHubFactory : WebApplicationFactory<Program>
{
    private readonly RSA _rsa = RSA.Create(2048);

    public string TestPrivateKeyPem { get; }
    public string TestPublicKeyPem  { get; }

    public FakeSefazClient SefazClient { get; } = new();
    public FakeDocumentJobQueue JobQueue { get; } = new();
    public FakeCancelamentoJobQueue CancelamentoJobQueue { get; } = new();
    public FakeSequenceManager SequenceManager { get; } = new();
    public FakePrefeituraClient PrefeituraClient { get; } = new();

    private static readonly string[] EnvVarKeys =
    [
        "CONNECTIONSTRINGS__DEFAULTCONNECTION",
        "JWT__ISSUER", "JWT__AUDIENCE", "JWT__EXPIRESINSECONDS",
        "JWT__PRIVATEKEYPEM", "JWT__PUBLICKEYPEMS__0",
        "ADMINKEY__VALUE", "CERT__ENCRYPTIONKEY",
        "HANGFIREDASHBOARD__USER", "HANGFIREDASHBOARD__PASSWORD"
    ];

    public VisuFiscalHubFactory()
    {
        TestPrivateKeyPem = _rsa.ExportRSAPrivateKeyPem();
        TestPublicKeyPem  = _rsa.ExportSubjectPublicKeyInfoPem();

        // Environment variables are read by WebApplication.CreateBuilder before DI runs.
        // Program.cs reads builder.Configuration (which includes env vars) during service
        // registration, BEFORE ConfigureWebHost.ConfigureAppConfiguration callbacks apply.
        // Therefore env vars are the only reliable way to inject configuration ahead of
        // AddInfrastructure and AddHealthChecks.
        Environment.SetEnvironmentVariable("CONNECTIONSTRINGS__DEFAULTCONNECTION",
            "Host=localhost;Database=test;Username=test;Password=test");
        Environment.SetEnvironmentVariable("JWT__ISSUER",           "visu-fiscal-hub");
        Environment.SetEnvironmentVariable("JWT__AUDIENCE",         "visu-fiscal-hub-clients");
        Environment.SetEnvironmentVariable("JWT__EXPIRESINSECONDS", "3600");
        Environment.SetEnvironmentVariable("JWT__PRIVATEKEYPEM",    TestPrivateKeyPem);
        Environment.SetEnvironmentVariable("JWT__PUBLICKEYPEMS__0", TestPublicKeyPem);
        Environment.SetEnvironmentVariable("ADMINKEY__VALUE",       "test-admin-key-1234567890");
        Environment.SetEnvironmentVariable("CERT__ENCRYPTIONKEY",   Convert.ToBase64String(new byte[32]));
        Environment.SetEnvironmentVariable("HANGFIREDASHBOARD__USER",     "test");
        Environment.SetEnvironmentVariable("HANGFIREDASHBOARD__PASSWORD", "test");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"]           = "visu-fiscal-hub",
                ["Jwt:Audience"]         = "visu-fiscal-hub-clients",
                ["Jwt:ExpiresInSeconds"] = "3600",
                ["Jwt:PrivateKeyPem"]    = TestPrivateKeyPem,
                ["Jwt:PublicKeyPems:0"]  = TestPublicKeyPem,
                ["AdminKey:Value"]       = "test-admin-key-1234567890",
                ["CERT:EncryptionKey"]   = Convert.ToBase64String(new byte[32]),
                ["HangfireDashboard:User"]     = "test",
                ["HangfireDashboard:Password"] = "test",
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=test;Username=test;Password=test",
            });
        });

        builder.ConfigureServices(services =>
        {
            // DbContext and Hangfire are already configured for the Test environment
            // inside DependencyInjection.AddInfrastructure (InMemory DB, no Hangfire server).

            // Replace ISefazClient with fake
            services.RemoveAll<ISefazClient>();
            services.AddSingleton<ISefazClient>(SefazClient);

            // Replace IPrefeituraClient with fake
            services.RemoveAll<IPrefeituraClient>();
            services.AddSingleton<IPrefeituraClient>(PrefeituraClient);

            // Replace IDocumentJobQueue with a no-op stub — DocumentJobQueue depends on
            // Hangfire's IBackgroundJobClient which requires a real storage in production.
            // In test environment Hangfire has no storage backend configured.
            services.RemoveAll<IDocumentJobQueue>();
            services.AddSingleton<IDocumentJobQueue>(JobQueue);

            // Replace ICancelamentoJobQueue with a no-op stub for the same reason.
            services.RemoveAll<ICancelamentoJobQueue>();
            services.AddSingleton<ICancelamentoJobQueue>(CancelamentoJobQueue);

            // Replace ISequenceManager with an in-memory stub — SequenceManager uses
            // ExecuteSqlRawAsync / SqlQueryRaw which are relational-specific EF methods
            // incompatible with the InMemory EF provider used in tests.
            services.RemoveAll<ISequenceManager>();
            services.AddSingleton<ISequenceManager>(SequenceManager);

            // Disable rate limiting to prevent test flakiness as the suite grows.
            // RemoveAll strips the production configure registrations (policies + RejectionStatusCode).
            // Keep in sync with the AddRateLimiter block in Program.cs.
            services.RemoveAll<IConfigureOptions<RateLimiterOptions>>();
            services.Configure<RateLimiterOptions>(opts =>
            {
                opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                opts.AddPolicy("auth",  _ => RateLimitPartition.GetNoLimiter("test"));
                opts.AddPolicy("api",   _ => RateLimitPartition.GetNoLimiter("test"));
                opts.AddPolicy("admin", _ => RateLimitPartition.GetNoLimiter("test"));
            });

            // Prevent Microsoft.IdentityModel from caching and disposing the RSA key
            // inside RsaSecurityKey after the first token validation.
            // CacheSignatureProviders = false creates a fresh CryptoProvider on each
            // validation call, avoiding the ObjectDisposedException on subsequent calls.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters.CryptoProviderFactory =
                    new CryptoProviderFactory { CacheSignatureProviders = false };
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Dispose own resources before calling base, which shuts down the test server.
            // _rsa is used only to generate PEM strings in the constructor — safe to dispose first.
            _rsa.Dispose();
            foreach (var key in EnvVarKeys)
                Environment.SetEnvironmentVariable(key, null);
        }
        base.Dispose(disposing);
    }
}
