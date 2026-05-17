using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.Interfaces;

namespace VisuFiscalHub.Infrastructure.Fiscal.Certificates;

internal sealed class TenantCertificateProvider : ITenantCertificateProvider
{
    // Armazena PFX decriptografado + senha em memória, NÃO o X509Certificate2.
    // X509Certificate2 é criado efêmeramente no ponto de uso — evita Dispose-while-in-use.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    // SemaphoreSlim por TenantId previne cache stampede: somente uma thread carrega do banco por vez.
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Locks = new();

    private readonly ITenantRepository _tenantRepo;
    private readonly ICertificateEncryptionService _encryptionService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TenantCertificateProvider> _logger;

    public TenantCertificateProvider(
        ITenantRepository tenantRepo,
        ICertificateEncryptionService encryptionService,
        IMemoryCache cache,
        ILogger<TenantCertificateProvider> logger)
    {
        _tenantRepo = tenantRepo;
        _encryptionService = encryptionService;
        _cache = cache;
        _logger = logger;
    }

    public async Task<Result<X509Certificate2>> GetCertificateAsync(
        TenantId tenantId,
        CancellationToken ct = default)
    {
        var cacheKey = $"cert:{tenantId.Value}";

        // Cache hit: verificar expiração antes de criar o objeto X509.
        if (_cache.TryGetValue(cacheKey, out CertificadoCache? cached) && cached is not null)
        {
            if (cached.Vencimento is not null && cached.Vencimento.Value <= DateTimeOffset.UtcNow)
            {
                _cache.Remove(cacheKey);
                _logger.LogWarning("Certificado do Tenant {TenantId} venceu — removido do cache.", tenantId.Value);
                return Result.Failure<X509Certificate2>(
                    new Error("Certificate.Vencido", "O certificado do Tenant está vencido."));
            }

            return CarregarX509(cached, tenantId);
        }

        // Cache miss: adquirir lock por TenantId para evitar stampede.
        var semaphore = Locks.GetOrAdd(tenantId.Value, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct);
        try
        {
            // Double-check após obter o lock.
            if (_cache.TryGetValue(cacheKey, out cached) && cached is not null)
                return CarregarX509(cached, tenantId);

            return await CarregarDoBancoAsync(tenantId, cacheKey, ct);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task<Result<X509Certificate2>> CarregarDoBancoAsync(
        TenantId tenantId,
        string cacheKey,
        CancellationToken ct)
    {
        var tenant = await _tenantRepo.GetByIdAsync(tenantId, ct);
        if (tenant is null || !tenant.IsActive)
            return Result.Failure<X509Certificate2>(
                new Error("Certificate.TenantNaoEncontrado", "Tenant não encontrado ou inativo."));

        if (tenant.CertificadoPfxCriptografado is null || tenant.CertificadoSenhaCriptografada is null)
            return Result.Failure<X509Certificate2>(
                new Error("Certificate.SemCertificado", "Tenant não possui certificado configurado."));

        if (tenant.CertificadoVencimento is not null && tenant.CertificadoVencimento <= DateTimeOffset.UtcNow)
            return Result.Failure<X509Certificate2>(
                new Error("Certificate.Vencido", "O certificado do Tenant está vencido."));

        var pfxResult = _encryptionService.Decrypt(tenant.CertificadoPfxCriptografado);
        if (pfxResult.IsFailure)
        {
            _logger.LogError("Falha ao decriptografar PFX do Tenant {TenantId}: {Error}",
                tenantId.Value, pfxResult.Error.Code);
            return Result.Failure<X509Certificate2>(pfxResult.Error);
        }

        var senhaResult = _encryptionService.DecryptToString(tenant.CertificadoSenhaCriptografada);
        if (senhaResult.IsFailure)
        {
            _logger.LogError("Falha ao decriptografar senha do certificado do Tenant {TenantId}: {Error}",
                tenantId.Value, senhaResult.Error.Code);
            return Result.Failure<X509Certificate2>(senhaResult.Error);
        }

        // Valida que o PFX é válido antes de armazenar no cache.
        try
        {
            using var testCert = X509CertificateLoader.LoadPkcs12(
                pfxResult.Value,
                senhaResult.Value,
                X509KeyStorageFlags.EphemeralKeySet);

            _ = testCert.GetRSAPrivateKey()
                ?? throw new InvalidOperationException("Certificado não possui chave privada RSA.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao validar certificado PFX do Tenant {TenantId}", tenantId.Value);
            return Result.Failure<X509Certificate2>(
                new Error("Certificate.ImportFailed", "Falha ao importar o certificado PFX."));
        }

        var entrada = new CertificadoCache(pfxResult.Value, senhaResult.Value, tenant.CertificadoVencimento);

        // TTL = mínimo entre CacheTtl e tempo até vencimento do certificado.
        var ttl = tenant.CertificadoVencimento is not null
            ? TimeSpan.FromTicks(Math.Min(
                CacheTtl.Ticks,
                (tenant.CertificadoVencimento.Value - DateTimeOffset.UtcNow).Ticks))
            : CacheTtl;

        if (ttl <= TimeSpan.Zero)
            return Result.Failure<X509Certificate2>(
                new Error("Certificate.Vencido", "O certificado do Tenant está vencido."));

        _cache.Set(cacheKey, entrada, new MemoryCacheEntryOptions().SetAbsoluteExpiration(ttl));

        return CarregarX509(entrada, tenantId);
    }

    private Result<X509Certificate2> CarregarX509(CertificadoCache entrada, TenantId tenantId)
    {
        try
        {
            // EphemeralKeySet: não persiste chave privada em disco; seguro em containers.
            // Cada caller recebe seu próprio objeto — sem Dispose compartilhado.
            var cert = X509CertificateLoader.LoadPkcs12(
                entrada.PfxBytes,
                entrada.Senha,
                X509KeyStorageFlags.EphemeralKeySet);
            return Result.Success(cert);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao instanciar X509Certificate2 para Tenant {TenantId}", tenantId.Value);
            return Result.Failure<X509Certificate2>(
                new Error("Certificate.ImportFailed", "Falha ao importar o certificado PFX."));
        }
    }

    private sealed record CertificadoCache(
        byte[] PfxBytes,
        string Senha,
        DateTimeOffset? Vencimento);
}
