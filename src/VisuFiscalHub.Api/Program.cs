using System.Security.Claims;
using Mediator;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using VisuFiscalHub.Api.Authentication;
using VisuFiscalHub.Application;
using VisuFiscalHub.Api;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Application.Tenants.Commands.CreateClienteApp;
using VisuFiscalHub.Application.Tenants.Commands.CreateTenant;
using VisuFiscalHub.Application.Tenants.Commands.RotateClienteAppSecret;
using VisuFiscalHub.Application.Tenants.Commands.UpdateTenantCertificate;
using VisuFiscalHub.Application.Tenants.Commands.UpdateTenantCsc;
using VisuFiscalHub.Application.Tenants.Queries.GetCertificadoStatus;
using VisuFiscalHub.Application.Tenants.Queries.GetTenant;
using VisuFiscalHub.Application.Tenants.Queries.ListTenants;
using VisuFiscalHub.Domain.Identifiers;
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

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUserContext, HttpContextCurrentUserContext>();

    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

    var jwtSection = builder.Configuration.GetSection("Jwt");
    var signingKeyBytes = System.Text.Encoding.UTF8.GetBytes(
        jwtSection["SigningKey"]
        ?? throw new InvalidOperationException("Jwt:SigningKey não configurado."));
    if (signingKeyBytes.Length < 32)
        throw new InvalidOperationException("Jwt:SigningKey deve ter no mínimo 32 bytes (256 bits).");

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtSection["Issuer"],
                ValidateAudience = true,
                ValidAudience = jwtSection["Audience"],
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(signingKeyBytes),
                NameClaimType = ClaimTypes.NameIdentifier,
            };
        });

    builder.Services.AddAuthorization();
    builder.Services.AddOpenApi();

    var app = builder.Build();

    app.UseExceptionHandler();

    if (app.Environment.IsDevelopment())
        app.MapOpenApi();

    app.UseHttpsRedirection();
    app.UseSerilogRequestLogging();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseMiddleware<TenantValidationMiddleware>();

    // ── ClienteApps ──────────────────────────────────────────────────────────
    var clienteApps = app.MapGroup("/cliente-apps").RequireAuthorization();

    clienteApps.MapPost("/", async (CreateClienteAppCommand command, IMediator mediator, CancellationToken ct) =>
        (await mediator.Send(command, ct)).ToHttpResult(r => Results.Created($"/cliente-apps/{r.Id}", r)));

    clienteApps.MapPost("/{id:guid}/rotate-secret", async (
        Guid id,
        ICurrentUserContext userCtx,
        IMediator mediator,
        CancellationToken ct) =>
    {
        var command = new RotateClienteAppSecretCommand { ClienteAppId = new ClienteAppId(id) };
        return (await mediator.Send(command, ct)).ToHttpResult(r => Results.Ok(r));
    });

    // ── Tenants ───────────────────────────────────────────────────────────────
    var tenants = app.MapGroup("/tenants").RequireAuthorization();

    tenants.MapPost("/", async (CreateTenantCommand command, ICurrentUserContext userCtx, IMediator mediator, CancellationToken ct) =>
    {
        var cmd = command with { ClienteAppId = userCtx.ClienteAppId };
        return (await mediator.Send(cmd, ct)).ToHttpResult(r => Results.Created($"/tenants/{r.Id}", r));
    });

    tenants.MapGet("/", async (
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
    });

    tenants.MapGet("/{id:guid}", async (
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
    });

    tenants.MapGet("/{id:guid}/certificado/status", async (
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
    });

    tenants.MapPut("/{id:guid}/certificado", async (
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
    });

    tenants.MapPut("/{id:guid}/csc", async (
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
    });

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

// Global exception handler — converte exceções não tratadas em ProblemDetails 500
// sem vazar stack traces para o cliente.
sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
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
