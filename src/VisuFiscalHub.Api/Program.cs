using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Mediator;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using VisuFiscalHub.Api.Authentication;
using VisuFiscalHub.Application;
using VisuFiscalHub.Api;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Application.Common.Security;
using VisuFiscalHub.Application.Tenants.Commands.CreateClienteApp;
using VisuFiscalHub.Application.Tenants.Commands.CreateTenant;
using VisuFiscalHub.Application.Tenants.Commands.RotateClienteAppSecret;
using VisuFiscalHub.Application.Tenants.Commands.UpdateTenantCertificate;
using VisuFiscalHub.Application.Tenants.Commands.UpdateTenantCsc;
using VisuFiscalHub.Application.Tenants.Queries.GetCertificadoStatus;
using VisuFiscalHub.Application.Tenants.Queries.GetTenant;
using VisuFiscalHub.Application.Tenants.Queries.ListTenants;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.Interfaces;
using VisuFiscalHub.Infrastructure;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, services, config) =>
        config.ReadFrom.Configuration(ctx.Configuration)
              .ReadFrom.Services(services)
              .Enrich.FromLogContext()
              .WriteTo.Console());

    // ── Application + Infrastructure ────────────────────────────────────────
    builder.Services.AddApplication();

    builder.Services
        .AddOptions<JwtSettings>()
        .Bind(builder.Configuration.GetSection(JwtSettings.SectionName))
        .Validate(s =>
            !string.IsNullOrWhiteSpace(s.PrivateKeyPem) && s.PublicKeyPems.Length > 0,
            "Jwt:PrivateKeyPem e Jwt:PublicKeyPems são obrigatórios.")
        .ValidateOnStart();

    builder.Services.AddInfrastructure(builder.Configuration);

    // ── HTTP helpers ─────────────────────────────────────────────────────────
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUserContext, HttpContextCurrentUserContext>();
    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

    // ── JWT RS256 ─────────────────────────────────────────────────────────────
    var jwtConfig = builder.Configuration.GetSection(JwtSettings.SectionName);
    var publicKeyPems = jwtConfig.GetSection("PublicKeyPems").Get<string[]>() ?? [];

    var signingKeys = publicKeyPems
        .Select(pem =>
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            return (SecurityKey)new RsaSecurityKey(rsa);
        })
        .ToList();

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtConfig["Issuer"],
                ValidateAudience = true,
                ValidAudience = jwtConfig["Audience"],
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = signingKeys,
                NameClaimType = ClaimTypes.NameIdentifier,
            };
        });

    builder.Services.AddAuthorization();

    // ── Rate Limiting ─────────────────────────────────────────────────────────
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        options.OnRejected = async (ctx, ct) =>
        {
            ctx.HttpContext.Response.Headers["Retry-After"] = "60";
            await ctx.HttpContext.Response.WriteAsJsonAsync(
                new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Muitas requisições.",
                    Detail = "Tente novamente em 60 segundos."
                }, ct);
        };

        options.AddPolicy("auth", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0
                }));

        options.AddPolicy("api", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                httpContext.User.FindFirstValue("client_id")
                    ?? httpContext.Connection.RemoteIpAddress?.ToString()
                    ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 100,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0
                }));

        options.AddPolicy("admin", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0
                }));
    });

    builder.Services.AddOpenApi();

    var app = builder.Build();

    app.UseExceptionHandler();

    if (app.Environment.IsDevelopment())
        app.MapOpenApi();

    app.UseHttpsRedirection();
    app.UseSerilogRequestLogging();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseMiddleware<TenantValidationMiddleware>();

    // ── POST /auth/token ──────────────────────────────────────────────────────
    app.MapPost("/auth/token",
        async (
            TokenRequest request,
            IClienteAppRepository clienteAppRepo,
            ITokenService tokenService,
            IOptions<JwtSettings> jwtOptions,
            CancellationToken ct) =>
        {
            var clienteApp = await clienteAppRepo.GetByClientIdAsync(request.ClientId, ct);

            if (clienteApp is null || !clienteApp.IsActive)
                return Results.Json(new { error = "invalid_client" }, statusCode: StatusCodes.Status401Unauthorized);

            if (!ClientSecretHasher.VerificarHash(request.ClientSecret, clienteApp.ClientSecretHash))
                return Results.Json(new { error = "invalid_client" }, statusCode: StatusCodes.Status401Unauthorized);

            var token = tokenService.GenerateToken(clienteApp.Id, clienteApp.ClientId);

            return Results.Ok(new
            {
                access_token = token,
                token_type = "Bearer",
                expires_in = jwtOptions.Value.ExpiresInSeconds
            });
        })
        .RequireRateLimiting("auth")
        .WithName("PostAuthToken");

    // ── ClienteApps ──────────────────────────────────────────────────────────
    var clienteApps = app.MapGroup("/api/v1/clientes");

    clienteApps.MapPost("/",
        async (CreateClienteAppCommand command, IMediator mediator, HttpContext ctx, CancellationToken ct) =>
        {
            var adminKey = app.Configuration["AdminKey:Value"]
                ?? throw new InvalidOperationException("AdminKey:Value não configurado.");
            var providedKey = ctx.Request.Headers["X-Admin-Key"].FirstOrDefault() ?? string.Empty;

            if (!AdminKeyHelper.VerifyAdminKey(providedKey, adminKey))
                return Results.Json(new { error = "invalid_key" }, statusCode: StatusCodes.Status401Unauthorized);

            return (await mediator.Send(command, ct))
                .ToHttpResult(r => Results.Created($"/api/v1/clientes/{r.Id}", r));
        })
        .RequireRateLimiting("admin");

    clienteApps.MapPost("/{id:guid}/rotate-secret",
        async (
            Guid id,
            HttpContext ctx,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var adminKey = app.Configuration["AdminKey:Value"]
                ?? throw new InvalidOperationException("AdminKey:Value não configurado.");
            var providedKey = ctx.Request.Headers["X-Admin-Key"].FirstOrDefault() ?? string.Empty;

            if (!AdminKeyHelper.VerifyAdminKey(providedKey, adminKey))
                return Results.Json(new { error = "invalid_key" }, statusCode: StatusCodes.Status401Unauthorized);

            var command = new RotateClienteAppSecretCommand { ClienteAppId = new ClienteAppId(id) };
            return (await mediator.Send(command, ct)).ToHttpResult(r => Results.Ok(r));
        })
        .RequireRateLimiting("admin");

    // ── Tenants ───────────────────────────────────────────────────────────────
    var tenants = app.MapGroup("/api/v1/tenants").RequireAuthorization();

    tenants.MapPost("/",
        async (CreateTenantCommand command, ICurrentUserContext userCtx, IMediator mediator, CancellationToken ct) =>
        {
            var cmd = command with { ClienteAppId = userCtx.ClienteAppId };
            return (await mediator.Send(cmd, ct))
                .ToHttpResult(r => Results.Created($"/api/v1/tenants/{r.Id}", r));
        })
        .RequireRateLimiting("api");

    tenants.MapGet("/",
        async (
            int? page,
            int? pageSize,
            ICurrentUserContext userCtx,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var query = new ListTenantsQuery
            {
                ClienteAppId = userCtx.ClienteAppId,
                Page = page ?? 1,
                PageSize = pageSize ?? 20
            };
            return (await mediator.Send(query, ct)).ToHttpResult();
        })
        .RequireRateLimiting("api");

    tenants.MapGet("/{id:guid}",
        async (
            Guid id,
            ICurrentUserContext userCtx,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var query = new GetTenantQuery
            {
                TenantId = new TenantId(id),
                ClienteAppId = userCtx.ClienteAppId
            };
            return (await mediator.Send(query, ct)).ToHttpResult();
        })
        .RequireRateLimiting("api");

    tenants.MapGet("/{id:guid}/certificado/status",
        async (
            Guid id,
            ICurrentUserContext userCtx,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var query = new GetCertificadoStatusQuery
            {
                TenantId = new TenantId(id),
                ClienteAppId = userCtx.ClienteAppId
            };
            return (await mediator.Send(query, ct)).ToHttpResult();
        })
        .RequireRateLimiting("api");

    tenants.MapPut("/{id:guid}/certificado",
        async (
            Guid id,
            UpdateTenantCertificateCommand command,
            ICurrentUserContext userCtx,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var cmd = command with
            {
                TenantId = new TenantId(id),
                ClienteAppId = userCtx.ClienteAppId
            };
            return (await mediator.Send(cmd, ct)).ToHttpResult();
        })
        .RequireRateLimiting("api");

    tenants.MapPut("/{id:guid}/csc",
        async (
            Guid id,
            UpdateTenantCscCommand command,
            ICurrentUserContext userCtx,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var cmd = command with
            {
                TenantId = new TenantId(id),
                ClienteAppId = userCtx.ClienteAppId
            };
            return (await mediator.Send(cmd, ct)).ToHttpResult();
        })
        .RequireRateLimiting("api");

    // ── Health ────────────────────────────────────────────────────────────────
    app.MapGet("/health", () => TypedResults.Ok(new { status = "Healthy" }));

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

internal sealed record TokenRequest(string ClientId, string ClientSecret);

sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled exception for {Method} {Path}",
            httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Erro interno do servidor.",
            Detail = "Ocorreu um erro inesperado. Por favor, tente novamente mais tarde."
        }, cancellationToken);

        return true;
    }
}

public partial class Program { }

// HMAC-normalize both sides before comparing to eliminate length oracle.
// FixedTimeEquals requires equal-length inputs; hashing with a fixed key produces
// same-length MACs regardless of input length while preserving timing safety.
static class AdminKeyHelper
{
    private static readonly byte[] _hmacKey = RandomNumberGenerator.GetBytes(32);

    public static bool VerifyAdminKey(string provided, string expected)
    {
        var enc = System.Text.Encoding.UTF8;
        var providedMac = HMACSHA256.HashData(_hmacKey, enc.GetBytes(provided));
        var expectedMac = HMACSHA256.HashData(_hmacKey, enc.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(providedMac, expectedMac);
    }
}
