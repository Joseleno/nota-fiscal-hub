using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VisuFiscalHub.Application.Common.Interfaces;

namespace VisuFiscalHub.Tests.Integration.Infrastructure;

public sealed class VisuFiscalHubFactory : WebApplicationFactory<Program>
{
    private readonly RSA _rsa = RSA.Create(2048);

    public string TestPrivateKeyPem { get; }
    public string TestPublicKeyPem  { get; }

    public FakeSefazClient SefazClient { get; } = new();
    public FakeDocumentJobQueue JobQueue { get; } = new();
    public FakeSequenceManager SequenceManager { get; } = new();

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

            // Replace IDocumentJobQueue with a no-op stub — DocumentJobQueue depends on
            // Hangfire's IBackgroundJobClient which requires a real storage in production.
            // In test environment Hangfire has no storage backend configured.
            services.RemoveAll<IDocumentJobQueue>();
            services.AddSingleton<IDocumentJobQueue>(JobQueue);

            // Replace ISequenceManager with an in-memory stub — SequenceManager uses
            // ExecuteSqlRawAsync / SqlQueryRaw which are relational-specific EF methods
            // incompatible with the InMemory EF provider used in tests.
            services.RemoveAll<ISequenceManager>();
            services.AddSingleton<ISequenceManager>(SequenceManager);
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _rsa.Dispose();
        base.Dispose(disposing);
    }
}
