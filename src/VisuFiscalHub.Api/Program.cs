using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Mediator;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;
using VisuFiscalHub.Api.Authentication;
using VisuFiscalHub.Api.Middleware;
using VisuFiscalHub.Application;
using VisuFiscalHub.Api;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Application.Common.Models;
using Hangfire;
using Hangfire.Dashboard;
using VisuFiscalHub.Application.Documents.Commands.IssueDocument;
using VisuFiscalHub.Application.Documents.Queries.GetDocumentStatus;
using VisuFiscalHub.Application.Documents.Queries.GetDocumentXml;
using VisuFiscalHub.Infrastructure.Jobs;
using VisuFiscalHub.Application.Common.Security;
using VisuFiscalHub.Application.Tenants.Commands.CreateClienteApp;
using VisuFiscalHub.Application.Tenants.Commands.CreateTenant;
using VisuFiscalHub.Application.Tenants.Commands.RotateClienteAppSecret;
using VisuFiscalHub.Application.Tenants.Commands.RotateClienteAppWebhookSecret;
using VisuFiscalHub.Application.Tenants.Commands.UpdateTenantCertificate;
using VisuFiscalHub.Application.Tenants.Commands.UpdateTenantCsc;
using VisuFiscalHub.Application.Tenants.Queries.GetCertificadoStatus;
using VisuFiscalHub.Application.Tenants.Queries.GetTenant;
using VisuFiscalHub.Application.Tenants.Queries.ListTenants;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using VisuFiscalHub.Infrastructure;
using VisuFiscalHub.Infrastructure.Persistence;

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
            !string.IsNullOrWhiteSpace(s.PrivateKeyPem)
            && s.PublicKeyPems.Length > 0
            && s.ExpiresInSeconds > 0,
            "Jwt: PrivateKeyPem, PublicKeyPems e ExpiresInSeconds > 0 são obrigatórios.")
        .ValidateOnStart();

    builder.Services
        .AddOptions<AdminKeySettings>()
        .Bind(builder.Configuration.GetSection(AdminKeySettings.SectionName))
        .Validate(s => !string.IsNullOrWhiteSpace(s.Value), "AdminKey:Value é obrigatório.")
        .ValidateOnStart();

    var hangfireOptions = builder.Services
        .AddOptions<HangfireDashboardSettings>()
        .Bind(builder.Configuration.GetSection(HangfireDashboardSettings.SectionName));

    if (!builder.Environment.IsDevelopment())
        hangfireOptions
            .Validate(
                s => !string.IsNullOrWhiteSpace(s.User) && !string.IsNullOrWhiteSpace(s.Password),
                "HangfireDashboard: User e Password são obrigatórios em produção.")
            .ValidateOnStart();

    builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

    // ── HTTP helpers ─────────────────────────────────────────────────────────
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUserContext, HttpContextCurrentUserContext>();
    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
    builder.Services.AddSingleton<HangfireDashboardAuthFilter>();

    // ── JWT RS256 ─────────────────────────────────────────────────────────────
    var jwtConfig = builder.Configuration.GetSection(JwtSettings.SectionName);
    var publicKeyPems = jwtConfig.GetSection("PublicKeyPems").Get<string[]>() ?? [];

    // RSA instances transferidas para RsaSecurityKey com ownership — registradas para
    // dispose no shutdown do host via IHostApplicationLifetime para evitar resource leak.
    var rsaInstances = new List<RSA>();
    var signingKeys = publicKeyPems
        .Select(pem =>
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            rsaInstances.Add(rsa);
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
                ClockSkew = TimeSpan.FromSeconds(30),
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
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

    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        // KnownProxies/KnownNetworks devem ser configurados com os CIDRs reais do ingress em produção.
        // Por segurança, limpar as defaults (qualquer rede) e adicionar explicitamente quando necessário.
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });

    builder.Services.AddOpenApi();

    var healthChecksBuilder = builder.Services.AddHealthChecks();
    if (!builder.Environment.IsEnvironment("Test"))
    {
        healthChecksBuilder.AddNpgSql(
            builder.Configuration.GetConnectionString("DefaultConnection")!,
            name: "postgresql",
            tags: ["ready"]);
    }

    var app = builder.Build();

    app.Lifetime.ApplicationStopped.Register(() =>
    {
        foreach (var rsa in rsaInstances)
            rsa.Dispose();
    });

    app.UseExceptionHandler();

    app.Use(async (ctx, next) =>
    {
        ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
        ctx.Response.Headers["X-Frame-Options"] = "DENY";
        ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
        await next();
    });

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference();
    }

    app.UseForwardedHeaders();

    if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Test"))
        app.UseHsts();

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
        async (
            CreateClienteAppCommand command,
            IMediator mediator,
            HttpContext ctx,
            IOptions<AdminKeySettings> adminOptions,
            CancellationToken ct) =>
        {
            var providedKey = ctx.Request.Headers["X-Admin-Key"].FirstOrDefault() ?? string.Empty;
            if (!AdminKeyHelper.VerifyAdminKey(providedKey, adminOptions.Value.Value))
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
            IOptions<AdminKeySettings> adminOptions,
            CancellationToken ct) =>
        {
            var providedKey = ctx.Request.Headers["X-Admin-Key"].FirstOrDefault() ?? string.Empty;
            if (!AdminKeyHelper.VerifyAdminKey(providedKey, adminOptions.Value.Value))
                return Results.Json(new { error = "invalid_key" }, statusCode: StatusCodes.Status401Unauthorized);

            var command = new RotateClienteAppSecretCommand { ClienteAppId = new ClienteAppId(id) };
            return (await mediator.Send(command, ct)).ToHttpResult();
        })
        .RequireRateLimiting("admin");

    clienteApps.MapPost("/{id:guid}/rotate-webhook-secret",
        async (
            Guid id,
            HttpContext ctx,
            IMediator mediator,
            IOptions<AdminKeySettings> adminOptions,
            CancellationToken ct) =>
        {
            var providedKey = ctx.Request.Headers["X-Admin-Key"].FirstOrDefault() ?? string.Empty;
            if (!AdminKeyHelper.VerifyAdminKey(providedKey, adminOptions.Value.Value))
                return Results.Json(new { error = "invalid_key" }, statusCode: StatusCodes.Status401Unauthorized);

            var command = new RotateClienteAppWebhookSecretCommand { ClienteAppId = new ClienteAppId(id) };
            return (await mediator.Send(command, ct)).ToHttpResult();
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
        .RequireRateLimiting("api")
        .AddEndpointFilter(async (ctx, next) =>
        {
            if (ctx.HttpContext.Request.ContentLength > 50 * 1024)
                return TypedResults.StatusCode(StatusCodes.Status413RequestEntityTooLarge);
            return await next(ctx);
        });

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

    // ── Documentos ───────────────────────────────────────────────────────────
    var documentos = app.MapGroup("/api/v1/documentos").RequireAuthorization();

    documentos.MapPost("/nfce",
        async (
            IssueNfceRequest request,
            HttpContext ctx,
            ICurrentUserContext userCtx,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var idempotencyKey = ctx.Request.Headers["X-Idempotency-Key"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(idempotencyKey))
                return Results.Problem(
                    detail: "DocumentoFiscal.IdempotencyKeyInvalida",
                    title: "X-Idempotency-Key é obrigatório.",
                    statusCode: StatusCodes.Status422UnprocessableEntity);

            var command = new IssueDocumentCommand
            {
                TenantId = userCtx.TenantId,
                ClienteAppId = userCtx.ClienteAppId,
                IdempotencyKey = idempotencyKey,
                Tipo = TipoDocumento.NfCe,
                Itens = request.Itens,
                Pagamentos = request.Pagamentos,
                Consumidor = request.Consumidor,
                IndPresenca = request.IndPresenca
            };

            return (await mediator.Send(command, ct))
                .ToHttpResult(r => Results.Accepted($"/api/v1/documentos/{r.DocumentoId.Value}/status", r));
        })
        .RequireRateLimiting("api");

    documentos.MapGet("/{id:guid}/status",
        async (
            Guid id,
            ICurrentUserContext userCtx,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var query = new GetDocumentStatusQuery
            {
                DocumentoId = new DocumentoFiscalId(id),
                ClienteAppId = userCtx.ClienteAppId
            };
            return (await mediator.Send(query, ct)).ToHttpResult();
        })
        .RequireRateLimiting("api");

    documentos.MapGet("/{id:guid}/xml",
        async (
            Guid id,
            ICurrentUserContext userCtx,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var query = new GetDocumentXmlQuery
            {
                DocumentoId = new DocumentoFiscalId(id),
                ClienteAppId = userCtx.ClienteAppId
            };
            return (await mediator.Send(query, ct))
                .ToHttpResult(xml => TypedResults.Ok(new { xmlAssinado = xml }) as IResult);
        })
        .RequireRateLimiting("api");

    documentos.MapPost("/{id:guid}/cancelar",
        (Guid id) => TypedResults.StatusCode(StatusCodes.Status501NotImplemented))
        .RequireRateLimiting("api");

    // ── Hangfire Dashboard + Recurring Jobs (skipped in Test environment) ──────
    if (!app.Environment.IsEnvironment("Test"))
    {
        // HangfireDashboardAuthFilter libera em Development; exige Basic Auth em outros ambientes.
        app.UseHangfireDashboard("/hangfire", new DashboardOptions
        {
            Authorization = [app.Services.GetRequiredService<HangfireDashboardAuthFilter>()]
        });

        RecurringJob.AddOrUpdate<OutboxRelayJob>(
            "outbox-relay",
            job => job.ExecuteAsync(CancellationToken.None),
            "*/30 * * * * *"); // A cada 30 segundos — latência de entrega webhook

        RecurringJob.AddOrUpdate<ReconciliacaoJobProcessor>(
            "reconciliacao-nfce",
            job => job.ExecuteAsync(CancellationToken.None),
            "*/5 * * * *"); // A cada 5 minutos
    }

    // ── Health ────────────────────────────────────────────────────────────────
    app.MapHealthChecks("/health");
    app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = _ => false
    });
    app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = hc => hc.Tags.Contains("ready")
    });

    if (!app.Environment.IsEnvironment("Test"))
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
    }

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

internal sealed record IssueNfceRequest(
    IReadOnlyList<ItemDocumentoDto> Itens,
    IReadOnlyList<PagamentoDto> Pagamentos,
    ConsumidorDto? Consumidor,
    int IndPresenca = 1);

public partial class Program { }
