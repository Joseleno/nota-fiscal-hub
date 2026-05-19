using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Infrastructure.Persistence;

namespace VisuFiscalHub.Tests.Integration.Infrastructure;

public sealed class VisuFiscalHubFactory : WebApplicationFactory<Program>
{
    public static readonly RSA TestRsa = RSA.Create(2048);

    public static readonly string TestPrivateKeyPem = TestRsa.ExportRSAPrivateKeyPem();
    public static readonly string TestPublicKeyPem  = TestRsa.ExportSubjectPublicKeyInfoPem();

    public FakeSefazClient SefazClient { get; } = new();

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
                ["DefaultConnection"]   = $"Host=localhost;Database=test_{Guid.NewGuid():N};Username=test;Password=test",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove real PostgreSQL DbContext and replace with InMemory
            var dbDescriptors = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>)
                         || d.ServiceType == typeof(ApplicationDbContext))
                .ToList();
            foreach (var d in dbDescriptors) services.Remove(d);

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase($"TestDb_{Guid.NewGuid():N}"));

            // Replace ISefazClient with fake
            services.RemoveAll<ISefazClient>();
            services.AddSingleton<ISefazClient>(SefazClient);

            // Remove Hangfire hosted services to avoid PostgreSQL connection on startup
            var hostedToRemove = services
                .Where(d => d.ImplementationType?.FullName?.Contains("Hangfire") == true
                         || d.ImplementationType?.FullName?.Contains("BackgroundJob") == true)
                .ToList();
            foreach (var d in hostedToRemove) services.Remove(d);
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) TestRsa.Dispose();
        base.Dispose(disposing);
    }
}
