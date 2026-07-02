# Spec Técnica — Fase 4: Autenticação JWT

**Versão:** 1.0  
**Data:** 2026-05-12  
**Dependências:** Fase 3 (Application Layer: ClienteApp e Tenant) concluída  
**Esforço estimado:** P (1–2 dias)

---

## 1. Visão Geral da Fase

### Objetivo

Implementar o fluxo Client Credentials com JWT RS256 auto-emitido. Inclui: `TokenService` (Infrastructure), `JwtBearerConfiguration` (Api), `TenantValidationMiddleware` (Api), endpoint `POST /auth/token`, e políticas de Rate Limiting para os três perfis de endpoint (`auth`, `api`, `admin`).

### Dependências

| Dependência | O que fornece |
|---|---|
| Fase 1 — Domain Layer | `ITokenService` (interface na Application layer), `ClienteApp` entity, `IClienteAppRepository` |
| Fase 2 — Banco de Dados | `IClienteAppRepository` concreto, `ITenantRepository` concreto |
| Fase 3 — Application | `ICurrentUserContext`, `HttpContextCurrentUserContext`, `ClienteAppErrors` |

### Critério de Conclusão

- `POST /auth/token` com credenciais válidas retorna JWT RS256 assinado
- JWT inválido resulta em `401 Unauthorized` em qualquer endpoint protegido
- `X-Tenant-Id` de Tenant não pertencente ao ClienteApp do JWT resulta em `403 Forbidden`
- Startup falha com `InvalidOperationException` se `JWT__PrivateKeyPem` não estiver configurado (`ValidateOnStart`)
- Rate limiting ativo no endpoint de token com header `Retry-After: 60` no 429
- `TokenValidationParameters.IssuerSigningKeys` (plural) aceita lista de múltiplas chaves para rotação zero-downtime

---

## 2. Arvore de Arquivos

```
src/
  VisuFiscalHub.Infrastructure/
    Services/
      TokenService.cs

  VisuFiscalHub.Api/
    Authentication/
      HttpContextCurrentUserContext.cs    (criado na Fase 3 — referência cruzada)
      JwtBearerConfiguration.cs
    Middleware/
      TenantValidationMiddleware.cs
      TenantContext.cs
    Endpoints/
      AuthEndpoints.cs
    Settings/
      JwtSettings.cs
    RateLimiting/
      RateLimitingConfiguration.cs
```

---

## 3. Especificação de Cada Arquivo

---

### 3.1 `Api/Settings/JwtSettings.cs`

**Namespace:** `VisuFiscalHub.Api.Settings`

**Usings:**
```csharp
using System.ComponentModel.DataAnnotations;
```

**Assinatura completa:**
```csharp
public sealed class JwtSettings
{
    public const string SectionName = "JWT";

    [Required(ErrorMessage = "JWT__PrivateKeyPem é obrigatório")]
    public string PrivateKeyPem { get; init; } = default!;

    // Lista para rotação zero-downtime: JWT__PublicKeyPems__0, JWT__PublicKeyPems__1, ...
    [Required(ErrorMessage = "JWT__PublicKeyPems deve conter ao menos uma chave pública")]
    [MinLength(1, ErrorMessage = "JWT__PublicKeyPems deve conter ao menos uma chave pública")]
    public string[] PublicKeyPems { get; init; } = default!;

    // TTL do token em segundos — padrão 3600 (1 hora)
    public int ExpirationSeconds { get; init; } = 3600;

    // Issuer e Audience opcionais — se preenchidos, são validados no token
    public string? Issuer { get; init; }
    public string? Audience { get; init; }
}
```

**Invariantes e regras críticas:**

- Registrado com `ValidateOnStart()` via `IOptions<JwtSettings>`:
  ```csharp
  builder.Services
      .AddOptions<JwtSettings>()
      .BindConfiguration(JwtSettings.SectionName)
      .ValidateDataAnnotations()
      .ValidateOnStart();
  ```
- Se `PrivateKeyPem` ou `PublicKeyPems` estiverem ausentes, o processo **falha no startup** antes de aceitar qualquer conexão.
- `PublicKeyPems` é um array para suporte a múltiplas chaves em rotação zero-downtime: a chave nova é adicionada, tokens antigos continuam sendo aceitos até expirar (TTL = 1h), depois a chave antiga é removida.
- As chaves devem ser fornecidas como PEM em formato raw (não Base64 adicional): `-----BEGIN RSA PRIVATE KEY-----\n...\n-----END RSA PRIVATE KEY-----`

---

### 3.2 `Infrastructure/Services/TokenService.cs`

**Namespace:** `VisuFiscalHub.Infrastructure.Services`

**Usings:**
```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using VisuFiscalHub.Api.Settings;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Entities;
```

**Assinatura completa:**
```csharp
public sealed class TokenService : ITokenService
{
    private readonly JwtSettings _settings;

    public TokenService(IOptions<JwtSettings> settings);

    public string GenerateToken(ClienteApp clienteApp);

    private RsaSecurityKey LoadPrivateKey();
}
```

**Invariantes e regras críticas:**

1. **Carregamento RSA obrigatório:**
   ```csharp
   private RsaSecurityKey LoadPrivateKey()
   {
       var rsa = RSA.Create();
       rsa.ImportFromPem(_settings.PrivateKeyPem);
       return new RsaSecurityKey(rsa);
   }
   ```
   - `RSA.Create()` seguido de `ImportFromPem` — este é o único caminho correto para chaves PEM brutas.
   - **Proibido:** `X509Certificate2.CreateFromPem` (destina-se a certificados, não a chaves PEM brutas).
   - **Proibido:** `RSA.Create(privateKeyParameters)` com importação manual de parâmetros RSA.

2. **Claims obrigatórios do JWT:**
   ```csharp
   var claims = new[]
   {
       new Claim(JwtRegisteredClaimNames.Sub, clienteApp.Id.Value.ToString()),
       new Claim("client_id", clienteApp.ClientId),
       new Claim(JwtRegisteredClaimNames.Iat,
           DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
           ClaimValueTypes.Integer64),
   };
   ```
   - `sub` = `clienteApp.Id.Value.ToString()` (o UUID do ClienteApp) — lido pelo `HttpContextCurrentUserContext`
   - `client_id` = `clienteApp.ClientId` (string pública) — lido pelo `TenantValidationMiddleware` e pelo rate limiter "api"
   - `iat` = Unix timestamp — inteiro para conformidade RFC 7519
   - `exp` calculado a partir de `_settings.ExpirationSeconds` (padrão 3600)

3. **Algoritmo de assinatura:**
   ```csharp
   var credentials = new SigningCredentials(
       LoadPrivateKey(),
       SecurityAlgorithms.RsaSha256);
   ```
   - RS256 obrigatório — nunca HS256.

4. **Montagem e serialização:**
   ```csharp
   var descriptor = new SecurityTokenDescriptor
   {
       Subject = new ClaimsIdentity(claims),
       Expires = DateTime.UtcNow.AddSeconds(_settings.ExpirationSeconds),
       SigningCredentials = credentials,
       Issuer = _settings.Issuer,
       Audience = _settings.Audience,
   };
   var handler = new JwtSecurityTokenHandler();
   var token = handler.CreateToken(descriptor);
   return handler.WriteToken(token);
   ```

5. O `LoadPrivateKey()` pode ser chamado a cada `GenerateToken` ou o objeto RSA pode ser cacheado como `Lazy<RsaSecurityKey>`. Atenção: `RSA` é `IDisposable` — se cacheado, **não** chamar `Dispose()` enquanto estiver em uso. Usar `Lazy<RsaSecurityKey>` inicializado no construtor é a abordagem mais segura.

---

### 3.3 `Api/Authentication/JwtBearerConfiguration.cs`

**Namespace:** `VisuFiscalHub.Api.Authentication`

**Usings:**
```csharp
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using VisuFiscalHub.Api.Settings;
```

**Assinatura completa:**
```csharp
public static class JwtBearerConfiguration
{
    public static IServiceCollection AddJwtBearerAuthentication(
        this IServiceCollection services,
        IConfiguration configuration);

    private static List<SecurityKey> LoadPublicKeys(string[] publicKeyPems);
}
```

**Invariantes e regras críticas:**

1. **Carregamento das chaves públicas para validação:**
   ```csharp
   private static List<SecurityKey> LoadPublicKeys(string[] publicKeyPems)
   {
       var keys = new List<SecurityKey>(publicKeyPems.Length);
       foreach (var pem in publicKeyPems)
       {
           var rsa = RSA.Create();
           rsa.ImportFromPem(pem);
           keys.Add(new RsaSecurityKey(rsa));
       }
       return keys;
   }
   ```
   - Cada chave pública em `PublicKeyPems` gera uma entrada na lista — **todas** são aceitas para validação.
   - **`IssuerSigningKeys` (plural) — nunca `IssuerSigningKey` (singular).**
   - Isso garante rotação zero-downtime: adicionar nova chave → aguardar TTL 1h → remover antiga.

2. **Configuração completa de `TokenValidationParameters`:**
   ```csharp
   var settings = /* resolver IOptions<JwtSettings> */;
   var keys = LoadPublicKeys(settings.PublicKeyPems);

   services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
       .AddJwtBearer(options =>
       {
           options.TokenValidationParameters = new TokenValidationParameters
           {
               ValidateIssuerSigningKey = true,
               IssuerSigningKeys = keys,  // PLURAL — aceita lista
               ValidateIssuer = settings.Issuer is not null,
               ValidIssuer = settings.Issuer,
               ValidateAudience = settings.Audience is not null,
               ValidAudience = settings.Audience,
               ValidateLifetime = true,
               ClockSkew = TimeSpan.FromSeconds(30),
           };
       });
   ```

3. O método `AddJwtBearerAuthentication` é uma extension method chamada em `Program.cs`. Recebe `IConfiguration` para obter `JwtSettings` via `configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()` — ou pode resolver via `IOptions<JwtSettings>` após o binding.

4. **Importante:** O `LoadPublicKeys` é chamado durante o startup. Se um PEM inválido for fornecido, `ImportFromPem` lança `CryptographicException` — o startup falha com erro descritivo, o que é o comportamento correto (fail-fast).

---

### 3.4 `Api/Middleware/TenantContext.cs`

**Namespace:** `VisuFiscalHub.Api.Middleware`

**Usings:**
```csharp
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;
```

**Assinatura completa:**
```csharp
// Record imutável — armazenado em HttpContext.Items["TenantContext"]
public sealed record TenantContext(
    TenantId TenantId,
    ClienteAppId ClienteAppId,
    RegimeTributario RegimeTributario,
    AmbienteSefaz Ambiente);
```

**Invariantes:**

- Record imutável por design — uma vez criado pelo middleware, não pode ser modificado.
- A chave no `HttpContext.Items` é exatamente a string `"TenantContext"` — uma constante pública para evitar strings mágicas:
  ```csharp
  public static class HttpContextItemKeys
  {
      public const string TenantContext = "TenantContext";
  }
  ```
- Lido por `HttpContextCurrentUserContext.TenantId` e pela infraestrutura de logging/enrichment.

---

### 3.5 `Api/Middleware/TenantValidationMiddleware.cs`

**Namespace:** `VisuFiscalHub.Api.Middleware`

**Usings:**
```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.Interfaces;
```

**Assinatura completa:**
```csharp
public sealed class TenantValidationMiddleware
{
    private readonly RequestDelegate _next;

    public TenantValidationMiddleware(RequestDelegate next);

    public async Task InvokeAsync(
        HttpContext context,
        ITenantRepository tenantRepository);
}
```

**Invariantes e regras críticas:**

1. **Rotas que dispensam validação de tenant** — o middleware deve verificar se o endpoint atual requer validação. Usar `IEndpointMetadata` ou uma lista de prefixos de rota que não necessitam de tenant:
   - `POST /auth/token` — não requer tenant
   - `POST /api/v1/clientes` — não requer tenant (protegido por X-Admin-Key)
   - `GET /health*` — não requer tenant
   - `GET /hangfire*` — não requer tenant
   - Todos os outros endpoints de API requerem tenant

2. **Extração do `client_id`:**
   ```csharp
   var clientId = context.User.FindFirstValue("client_id");
   if (string.IsNullOrEmpty(clientId))
   {
       // JWT sem claim client_id — não autenticado ou token malformado
       // Deixar o middleware de auth lidar com 401
       await _next(context);
       return;
   }
   ```

3. **Leitura do `X-Tenant-Id`:**
   ```csharp
   var tenantIdHeader = context.Request.Headers["X-Tenant-Id"].FirstOrDefault();
   if (string.IsNullOrEmpty(tenantIdHeader) || !Guid.TryParse(tenantIdHeader, out var tenantGuid))
   {
       context.Response.StatusCode = StatusCodes.Status400BadRequest;
       await context.Response.WriteAsJsonAsync(new ProblemDetails
       {
           Title = "X-Tenant-Id header inválido ou ausente",
           Status = 400
       });
       return;
   }
   ```
   - `X-Tenant-Id` contém o **UUID interno** do Tenant — nunca o CNPJ.
   - Se ausente ou não-Guid, retornar `400 Bad Request`.

4. **Carregamento e validação do Tenant:**
   ```csharp
   var tenantId = new TenantId(tenantGuid);
   var tenant = await tenantRepository.GetByIdAsync(tenantId, context.RequestAborted);
   if (tenant is null)
   {
       context.Response.StatusCode = StatusCodes.Status404NotFound;
       await context.Response.WriteAsJsonAsync(new ProblemDetails
       {
           Title = "Tenant não encontrado",
           Status = 404
       });
       return;
   }
   ```

5. **Validação de pertencimento:**
   ```csharp
   // Verificar que o ClienteApp do JWT é o dono do Tenant
   var clienteAppIdFromJwt = context.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
   if (!Guid.TryParse(clienteAppIdFromJwt, out var clienteAppGuid)
       || tenant.ClienteAppId.Value != clienteAppGuid)
   {
       context.Response.StatusCode = StatusCodes.Status403Forbidden;
       await context.Response.WriteAsJsonAsync(new ProblemDetails
       {
           Title = "Tenant não pertence ao ClienteApp autenticado",
           Status = 403
       });
       return;
   }
   ```

6. **Tenant inativo:**
   ```csharp
   if (!tenant.IsActive)
   {
       context.Response.StatusCode = StatusCodes.Status403Forbidden;
       await context.Response.WriteAsJsonAsync(new ProblemDetails
       {
           Title = "Tenant inativo",
           Status = 403
       });
       return;
   }
   ```

7. **Armazenar `TenantContext`:**
   ```csharp
   var tenantContext = new TenantContext(
       TenantId: tenant.Id,
       ClienteAppId: tenant.ClienteAppId,
       RegimeTributario: tenant.ConfiguracaoFiscal.Crt,
       Ambiente: tenant.ConfiguracaoFiscal.Ambiente);

   context.Items[HttpContextItemKeys.TenantContext] = tenantContext;
   await _next(context);
   ```

8. **Injeção de `ITenantRepository`:** O middleware recebe o repositório via `InvokeAsync` (injeção por método — padrão ASP.NET Core Middleware para serviços Scoped). O `RequestDelegate _next` é injetado no construtor (Singleton-safe).

9. **Posicionamento no pipeline:** O middleware deve ser registrado APÓS `app.UseAuthentication()` e `app.UseAuthorization()` — depende do JWT já ter sido validado para ler `context.User`.

---

### 3.6 `Api/Endpoints/AuthEndpoints.cs`

**Namespace:** `VisuFiscalHub.Api.Endpoints`

**Usings:**
```csharp
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Interfaces;
```

**Assinatura completa:**
```csharp
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(
        this IEndpointRouteBuilder routes);
}
```

**Contrato do endpoint `POST /auth/token`:**

```
Request body (application/json):
{
    "client_id": "string",
    "client_secret": "string",
    "grant_type": "client_credentials"
}

Responses:
  200 OK:
  {
      "access_token": "eyJ...",
      "token_type": "Bearer",
      "expires_in": 3600
  }

  400 Bad Request:  grant_type != "client_credentials"
  401 Unauthorized: credenciais inválidas
  {
      "error": "invalid_client",
      "error_description": "client_id ou client_secret inválidos"
  }
  429 Too Many Requests:
      Header: Retry-After: 60
```

**Implementação do handler:**

```csharp
routes.MapPost("/auth/token", async (
    [FromBody] TokenRequest request,
    IClienteAppRepository clienteAppRepository,
    ITokenService tokenService,
    CancellationToken cancellationToken) =>
{
    // 1. Validar grant_type
    if (request.GrantType != "client_credentials")
        return TypedResults.BadRequest(new ProblemDetails
        {
            Title = "grant_type inválido",
            Detail = "Apenas 'client_credentials' é suportado"
        });

    // 2. Buscar ClienteApp pelo client_id
    var clienteApp = await clienteAppRepository
        .GetByClientIdAsync(request.ClientId, cancellationToken);

    if (clienteApp is null || !clienteApp.IsActive)
        return TypedResults.Unauthorized();  // Não revelar se client_id existe

    // 3. Verificar client_secret via PBKDF2
    if (!VerificarClientSecret(request.ClientSecret, clienteApp.ClientSecretHash))
        return TypedResults.Unauthorized();

    // 4. Gerar JWT
    var token = tokenService.GenerateToken(clienteApp);
    return TypedResults.Ok(new TokenResponse(token, "Bearer", 3600));
})
.WithName("PostToken")
.RequireRateLimiting("auth")
.AllowAnonymous();
```

**Função `VerificarClientSecret`:**

```csharp
private static bool VerificarClientSecret(string clientSecret, string storedHash)
{
    // storedHash formato: "base64Salt:base64Hash"
    var parts = storedHash.Split(':');
    if (parts.Length != 2) return false;

    var salt = Convert.FromBase64String(parts[0]);
    var storedHashBytes = Convert.FromBase64String(parts[1]);

    var computedHash = Rfc2898DeriveBytes.Pbkdf2(
        password: clientSecret,
        salt: salt,
        iterations: 600_000,
        hashAlgorithm: HashAlgorithmName.SHA256,
        outputLength: 32);

    // Comparação time-safe para prevenir timing attacks
    return CryptographicOperations.FixedTimeEquals(computedHash, storedHashBytes);
}
```

**DTOs locais:**

```csharp
public sealed record TokenRequest(
    [property: JsonPropertyName("client_id")] string ClientId,
    [property: JsonPropertyName("client_secret")] string ClientSecret,
    [property: JsonPropertyName("grant_type")] string GrantType);

public sealed record TokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn);
```

**Invariantes de segurança:**

- A resposta `401` **não diferencia** entre "client_id não encontrado" e "client_secret incorreto" — ambos retornam `{ "error": "invalid_client" }`. Isso previne enumeração de client_ids.
- Verificação PBKDF2 usa `CryptographicOperations.FixedTimeEquals` — prevenção de timing attack.
- Endpoint não requer `Authorization` header — usa `AllowAnonymous()`.
- Rate limiter "auth" aplicado: 10 requisições por minuto por IP.

---

### 3.7 `Api/RateLimiting/RateLimitingConfiguration.cs`

**Namespace:** `VisuFiscalHub.Api.RateLimiting`

**Usings:**
```csharp
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
```

**Assinatura completa:**
```csharp
public static class RateLimitingConfiguration
{
    public static IServiceCollection AddRateLimitingPolicies(
        this IServiceCollection services);
}
```

**Três políticas obrigatórias:**

#### Política "auth" — `POST /auth/token`

```csharp
options.AddFixedWindowLimiter("auth", limiterOptions =>
{
    limiterOptions.PermitLimit = 10;
    limiterOptions.Window = TimeSpan.FromMinutes(1);
    limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    limiterOptions.QueueLimit = 0;  // Sem fila — rejeitar imediatamente quando limite atingido
});
```

- Partição por **IP** do cliente (`context.Connection.RemoteIpAddress?.ToString() ?? "unknown"`).
- 10 requisições por minuto por IP.

#### Política "api" — endpoints autenticados (emissão de documentos, etc.)

```csharp
options.AddFixedWindowLimiter("api", limiterOptions =>
{
    limiterOptions.PermitLimit = 100;
    limiterOptions.Window = TimeSpan.FromMinutes(1);
    limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    limiterOptions.QueueLimit = 0;
});
```

- Partição por **`client_id`** do JWT — `context.User.FindFirstValue("client_id") ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown"`.
- Fallback para IP quando `client_id` não disponível (request não autenticado).
- 100 requisições por minuto por `client_id`.

#### Política "admin" — `POST /api/v1/clientes`

```csharp
options.AddFixedWindowLimiter("admin", limiterOptions =>
{
    limiterOptions.PermitLimit = 5;
    limiterOptions.Window = TimeSpan.FromMinutes(1);
    limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    limiterOptions.QueueLimit = 0;
});
```

- Partição por **IP** do cliente.
- 5 requisições por minuto por IP — proteção do endpoint de criação de ClienteApp.

#### Response `429 Too Many Requests`

```csharp
options.OnRejected = async (context, cancellationToken) =>
{
    context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
    context.HttpContext.Response.Headers.RetryAfter = "60";
    await context.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
    {
        Title = "Muitas requisições",
        Detail = "Limite de taxa atingido. Tente novamente em 60 segundos.",
        Status = 429
    }, cancellationToken);
};
```

**Invariante crítica:** `Retry-After: 60` deve estar **sempre** presente no response 429 — conforme `decisions.md` RNF-03.

---

## 4. Configuração em `Program.cs`

A seguir o bloco de configuração completo que `Program.cs` deve conter para esta fase (a ser integrado ao `Program.cs` da Fase 8):

```csharp
// 1. Settings com ValidateOnStart
builder.Services
    .AddOptions<JwtSettings>()
    .BindConfiguration(JwtSettings.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// 2. HttpContextAccessor (necessário para HttpContextCurrentUserContext)
builder.Services.AddHttpContextAccessor();

// 3. ICurrentUserContext (Scoped)
builder.Services.AddScoped<ICurrentUserContext, HttpContextCurrentUserContext>();

// 4. TokenService (Infrastructure → registrado em AddInfrastructure)
// builder.Services.AddScoped<ITokenService, TokenService>();

// 5. JWT Bearer Authentication
builder.Services.AddJwtBearerAuthentication(builder.Configuration);

// 6. Rate Limiting
builder.Services.AddRateLimitingPolicies();

// --- Pipeline (app.Use...) ---

// 7. Authentication + Authorization (ordem obrigatória)
app.UseAuthentication();
app.UseAuthorization();

// 8. TenantValidationMiddleware — APÓS auth para ter context.User populado
app.UseMiddleware<TenantValidationMiddleware>();

// 9. Rate Limiter
app.UseRateLimiter();

// 10. Endpoints
app.MapAuthEndpoints();
```

---

## 5. Fluxo de Dados

### 5.1 Fluxo `POST /auth/token`

```
POST /auth/token
  Body: { client_id, client_secret, grant_type: "client_credentials" }
        │
        ▼
  Rate Limiter "auth" (FixedWindow 10/min por IP)
  ├── Dentro do limite → continua
  └── Acima do limite → 429 + Retry-After: 60
        │
        ▼
  AuthEndpoints.Handler
  ├── grant_type != "client_credentials" → 400 Bad Request
  ├── _clienteAppRepository.GetByClientIdAsync(clientId)
  │     └── null ou IsActive=false → 401 { "error": "invalid_client" }
  ├── VerificarClientSecret(clientSecret, clienteApp.ClientSecretHash)
  │     ├── Rfc2898DeriveBytes.Pbkdf2(clientSecret, salt, 600_000, SHA256)
  │     ├── CryptographicOperations.FixedTimeEquals(computed, stored)
  │     └── false → 401 { "error": "invalid_client" }
  └── tokenService.GenerateToken(clienteApp)
        ├── RSA.Create() + ImportFromPem(privateKeyPem)
        ├── Claims: sub=clienteAppId, client_id=clientId, iat, exp
        ├── Assinar com RS256
        └── Serializar JWT string
        │
        ▼
  200 OK
  { "access_token": "eyJ...", "token_type": "Bearer", "expires_in": 3600 }
```

---

### 5.2 Fluxo de Requisição Autenticada com Validação de Tenant

```
POST /api/v1/documentos/nfce
  Headers:
    Authorization: Bearer eyJ...
    X-Tenant-Id: {tenant-uuid}
    X-Idempotency-Key: {uuid}
        │
        ▼
  Rate Limiter "api" (FixedWindow 100/min por client_id)
        │
        ▼
  app.UseAuthentication()
  ├── JwtBearerMiddleware valida assinatura RS256
  │     ├── foreach PublicKeyPems: RSA.Create() + ImportFromPem → IssuerSigningKeys
  │     ├── Valida exp, iat, issuer, audience (se configurados)
  │     ├── JWT válido → popula context.User com claims
  │     └── JWT inválido/expirado → 401 Unauthorized
        │
        ▼
  app.UseAuthorization()
  (verifica policies de autorização — [Authorize] no endpoint)
        │
        ▼
  TenantValidationMiddleware.InvokeAsync
  ├── context.User.FindFirstValue("client_id") → clientId (do JWT)
  ├── X-Tenant-Id header → parse como Guid
  │     └── inválido/ausente → 400 Bad Request
  ├── _tenantRepository.GetByIdAsync(tenantId)
  │     └── null → 404 Not Found
  ├── tenant.ClienteAppId.Value == clienteAppId from sub claim
  │     └── diferente → 403 Forbidden
  ├── tenant.IsActive
  │     └── false → 403 Forbidden
  └── HttpContext.Items["TenantContext"] = new TenantContext(tenantId, clienteAppId, ...)
        │
        ▼
  Endpoint Handler → IMediator.Send(command)
  └── Command populado com:
        ICurrentUserContext.ClienteAppId ← JWT claim "sub"
        ICurrentUserContext.TenantId     ← HttpContext.Items["TenantContext"].TenantId
```

---

### 5.3 Fluxo de Rotação de Chaves JWT (Zero-Downtime)

```
Estado inicial:
  JWT__PublicKeyPems__0 = "chave_antiga"
  IssuerSigningKeys = [ RsaSecurityKey(chave_antiga) ]

Passo 1 — Adicionar nova chave:
  JWT__PublicKeyPems__0 = "chave_antiga"
  JWT__PublicKeyPems__1 = "chave_nova"
  JWT__PrivateKeyPem    = "chave_nova_privada"
  → IssuerSigningKeys = [ RsaSecurityKey(chave_antiga), RsaSecurityKey(chave_nova) ]
  → Novos tokens assinados com chave_nova
  → Tokens antigos (assinados com chave_antiga) continuam sendo aceitos

Passo 2 — Aguardar TTL expirar (1 hora — todos os tokens antigos expiram):

Passo 3 — Remover chave antiga:
  JWT__PublicKeyPems__0 = "chave_nova"
  → IssuerSigningKeys = [ RsaSecurityKey(chave_nova) ]
  → Tokens antigos (chave_antiga) já expiraram — sem impacto
```

---

## 6. Checklist de Conclusão

### JwtSettings
- [ ] `PrivateKeyPem` e `PublicKeyPems` declarados com `[Required]`
- [ ] `ValidateOnStart()` configurado — startup falha se ausente
- [ ] `PublicKeyPems` é `string[]` (array) — não `string` singular
- [ ] `ExpirationSeconds` com padrão 3600

### TokenService
- [ ] Carrega RSA com `RSA.Create()` + `rsa.ImportFromPem(privateKeyPem)` — não usa `X509Certificate2`
- [ ] Claim `sub` = `clienteApp.Id.Value.ToString()` (UUID do ClienteApp)
- [ ] Claim `client_id` = `clienteApp.ClientId` (string pública)
- [ ] Algoritmo RS256 (`SecurityAlgorithms.RsaSha256`)
- [ ] `exp` baseado em `_settings.ExpirationSeconds`
- [ ] `iat` presente como Unix timestamp inteiro

### JwtBearerConfiguration
- [ ] `TokenValidationParameters.IssuerSigningKeys` (plural) — lista de todas as chaves
- [ ] Foreach sobre `settings.PublicKeyPems` → `RSA.Create()` + `ImportFromPem` por chave
- [ ] `ValidateLifetime = true`
- [ ] `ClockSkew = TimeSpan.FromSeconds(30)`

### TenantContext
- [ ] Record imutável com `TenantId`, `ClienteAppId`, `RegimeTributario`, `Ambiente`
- [ ] Chave `"TenantContext"` definida como constante (não string mágica)

### TenantValidationMiddleware
- [ ] Rotas sem tenant (auth, clientes admin, health) são ignoradas pelo middleware
- [ ] `X-Tenant-Id` inválido → `400 Bad Request`
- [ ] Tenant não encontrado → `404 Not Found`
- [ ] Tenant de outro ClienteApp → `403 Forbidden`
- [ ] Tenant inativo → `403 Forbidden`
- [ ] `TenantContext` armazenado em `HttpContext.Items["TenantContext"]`
- [ ] `ITenantRepository` injetado via `InvokeAsync` (não construtor) — Scoped em Singleton
- [ ] Posicionamento: APÓS `UseAuthentication()` e `UseAuthorization()`

### AuthEndpoints
- [ ] `POST /auth/token` sem autenticação JWT (`AllowAnonymous`)
- [ ] `grant_type` validado — apenas `client_credentials`
- [ ] `VerificarClientSecret` usa PBKDF2 (600.000 iterações, SHA-256)
- [ ] `CryptographicOperations.FixedTimeEquals` para comparação de hash
- [ ] 401 não diferencia "client_id não existe" de "secret errado"
- [ ] Rate limiter "auth" aplicado ao endpoint

### Rate Limiting
- [ ] "auth": 10/min por IP — `POST /auth/token`
- [ ] "api": 100/min por `client_id` — endpoints autenticados
- [ ] "admin": 5/min por IP — `POST /api/v1/clientes`
- [ ] `OnRejected` retorna `429` com `Retry-After: 60` header
- [ ] `ProblemDetails` no body do 429

### Program.cs
- [ ] `AddOptions<JwtSettings>().ValidateOnStart()`
- [ ] `AddHttpContextAccessor()`
- [ ] `AddScoped<ICurrentUserContext, HttpContextCurrentUserContext>()`
- [ ] `AddJwtBearerAuthentication(builder.Configuration)`
- [ ] `AddRateLimitingPolicies()`
- [ ] `app.UseAuthentication()` antes de `UseAuthorization()`
- [ ] `app.UseMiddleware<TenantValidationMiddleware>()` após auth
- [ ] `app.UseRateLimiter()`

### Testes de Integração (Fase 10 — referência antecipada)
- [ ] `POST /auth/token` com credenciais válidas → 200 com JWT RS256
- [ ] `POST /auth/token` com secret errado → 401
- [ ] `POST /auth/token` acima do rate limit → 429 com `Retry-After: 60`
- [ ] Endpoint protegido sem JWT → 401
- [ ] Endpoint protegido com JWT expirado → 401
- [ ] `X-Tenant-Id` de outro ClienteApp → 403
- [ ] Startup falha se `JWT__PrivateKeyPem` ausente
