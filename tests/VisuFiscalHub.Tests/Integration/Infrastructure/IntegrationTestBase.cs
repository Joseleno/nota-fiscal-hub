using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using VisuFiscalHub.Application.Common.Security;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.ValueObjects;
using VisuFiscalHub.Infrastructure.Persistence;

namespace VisuFiscalHub.Tests.Integration.Infrastructure;

public abstract class IntegrationTestBase : IDisposable
{
    protected readonly VisuFiscalHubFactory Factory;
    protected readonly HttpClient Client;
    protected readonly FakeSefazClient SefazFake;
    protected readonly FakeDocumentJobQueue JobQueue;
    protected readonly FakeSequenceManager SequenceManager;

    protected IntegrationTestBase()
    {
        Factory = new VisuFiscalHubFactory();
        Client = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        SefazFake       = Factory.SefazClient;
        JobQueue        = Factory.JobQueue;
        SequenceManager = Factory.SequenceManager;
        JobQueue.Reset();
        SequenceManager.Reset();
    }

    protected async Task<(ClienteAppId Id, string ClientId, string ClientSecret)> CriarClienteAppAsync()
    {
        var clientId = $"cli_{Guid.NewGuid():N}";
        var clientSecret = Guid.NewGuid().ToString("N");
        var clientSecretHash = ClientSecretHasher.DerivarHash(clientSecret);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        var app = ClienteApp.Criar(
            name: "Test ClienteApp",
            clientId: clientId,
            clientSecretHash: clientSecretHash,
            timeProvider: timeProvider).Value;

        db.ClienteApps.Add(app);
        await db.SaveChangesAsync();

        return (app.Id, clientId, clientSecret);
    }

    protected async Task<string> ObterTokenAsync(string clientId, string clientSecret)
    {
        using var response = await Client.PostAsJsonAsync("/auth/token", new { clientId, clientSecret });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<TokenBody>();
        return body!.AccessToken;
    }

    protected HttpClient CriarClienteAutenticado(string token, Guid? tenantId = null)
    {
        var client = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (tenantId.HasValue)
            client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.Value.ToString());
        return client;
    }

    protected async Task<TenantId> CriarTenantAsync(ClienteAppId clienteAppId, string cnpj = "11222333000181")
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        var configuracao = ConfiguracaoFiscal.Criar(
            crt: RegimeTributario.SimplesNacional,
            serie: "001",
            ambiente: AmbienteSefaz.Homologacao,
            ufCodigo: 35).Value;

        var endereco = Endereco.Criar(
            logradouro: "Rua Teste",
            numero: "100",
            complemento: null,
            bairro: "Centro",
            municipio: "São Paulo",
            codigoMunicipio: 3550308,
            uf: "SP",
            cep: "01310100").Value;

        var tenant = Tenant.Criar(
            clienteAppId: clienteAppId,
            cnpj: Cnpj.Criar(cnpj).Value,
            razaoSocial: "Empresa Teste LTDA",
            nomeFantasia: null,
            configuracaoFiscal: configuracao,
            endereco: endereco,
            timeProvider: timeProvider).Value;

        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        return tenant.Id;
    }

    // Gera JWT assinado com a chave privada do factory mas com exp no passado.
    // Permite testar que ValidateLifetime = true rejeita tokens vencidos com 401.
    protected string GerarTokenExpirado()
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(Factory.TestPrivateKeyPem);
        var key = new RsaSecurityKey(rsa) { CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false } };
        var credentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
        var past = DateTimeOffset.UtcNow.AddHours(-2);
        var token = new JwtSecurityToken(
            issuer: "visu-fiscal-hub",
            audience: "visu-fiscal-hub-clients",
            claims: [new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString())],
            notBefore: past.AddHours(-1).UtcDateTime,
            expires: past.UtcDateTime,
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // Gera JWT assinado com uma chave RSA arbitrária (diferente da registrada no factory).
    // Permite testar que ValidateIssuerSigningKey = true rejeita tokens forjados com 401.
    protected static string GerarTokenComChaveErrada()
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false } };
        var credentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
        var now = DateTimeOffset.UtcNow;
        var token = new JwtSecurityToken(
            issuer: "visu-fiscal-hub",
            audience: "visu-fiscal-hub-clients",
            claims: [new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString())],
            notBefore: now.UtcDateTime,
            expires: now.AddHours(1).UtcDateTime,
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // TributoDto minimal válido para Simples Nacional (CSOSN 400 — sem cálculo de ICMS).
    // CstPisCofins.Cst07 = 7 (PISNT/COFINSNT — Simples Nacional).
    protected static object TributoSimples() => new
    {
        tipoIcms = (int)TipoIcms.CSOSN,
        csosnOuCst = 400,
        aliquotaIcms = 0.0,
        baseCalculoIcms = 0.0,
        valorIcms = 0.0,
        cstPis = (int)CstPisCofins.Cst07,
        baseCalculoPis = 0.0,
        aliquotaPis = 0.0,
        valorPis = 0.0,
        cstCofins = (int)CstPisCofins.Cst07,
        baseCalculoCofins = 0.0,
        aliquotaCofins = 0.0,
        valorCofins = 0.0
    };

    // Body mínimo que satisfaz todos os invariantes de domínio: totais balanceados (item == pagamento),
    // NCM com 8 dígitos (esquema SEFAZ §4.2.3), indPresenca 1 (Operação Presencial).
    protected static object BodyValido(decimal valor = 10.00m) => new
    {
        itens = new[]
        {
            new
            {
                codigoProduto = "PROD001",
                descricao = "Produto Teste",
                ncm = "12345678",
                cest = (string?)null,
                cfopSaida = "5102",
                unidadeComercial = "UN",
                quantidade = (double)1m,
                valorUnitario = (double)valor,
                valorDesconto = 0.0,
                origemMercadoria = 0,  // OrigemMercadoria.Nacional
                tributo = TributoSimples()
            }
        },
        pagamentos = new[]
        {
            new { tipoPagamento = (int)TipoPagamento.Dinheiro, valor = (double)valor }
        },
        consumidor = (object?)null,
        indPresenca = 1
    };

    // Espelha DocumentoStatusResponse — campos opcionais nulos enquanto o documento não for processado.
    protected sealed record StatusResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("documentoId")] DocumentoIdDto DocumentoId,
        [property: System.Text.Json.Serialization.JsonPropertyName("status")] int Status,
        [property: System.Text.Json.Serialization.JsonPropertyName("chaveAcesso")] string? ChaveAcesso,
        [property: System.Text.Json.Serialization.JsonPropertyName("qrCode")] string? QrCode,
        [property: System.Text.Json.Serialization.JsonPropertyName("protocolo")] string? Protocolo,
        [property: System.Text.Json.Serialization.JsonPropertyName("motivoRejeicao")] string? MotivoRejeicao,
        [property: System.Text.Json.Serialization.JsonPropertyName("authorizedAt")] DateTimeOffset? AuthorizedAt);

    // DTO para deserializar a resposta 202 de emissão de documento.
    // DocumentoFiscalId serializa como {"value": "<guid>"} pois é um readonly record struct
    // sem JsonConverter personalizado registrado.
    // Status é int pois não há JsonStringEnumConverter configurado no projeto.
    protected sealed record IssueResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("documentoId")] DocumentoIdDto DocumentoId,
        [property: System.Text.Json.Serialization.JsonPropertyName("status")] int Status,
        [property: System.Text.Json.Serialization.JsonPropertyName("chaveAcesso")] string? ChaveAcesso,
        [property: System.Text.Json.Serialization.JsonPropertyName("pollUrl")] string PollUrl,
        [property: System.Text.Json.Serialization.JsonPropertyName("createdAt")] DateTimeOffset CreatedAt);

    protected sealed record DocumentoIdDto(
        [property: System.Text.Json.Serialization.JsonPropertyName("value")] Guid Value);

    protected sealed record TokenBody(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("token_type")] string TokenType,
        [property: System.Text.Json.Serialization.JsonPropertyName("expires_in")] int ExpiresIn);

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
    }
}
