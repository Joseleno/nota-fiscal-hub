using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Errors;
using VisuFiscalHub.Domain.Events;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.ValueObjects;

namespace VisuFiscalHub.Domain.Entities;

public sealed class Tenant : Entity<TenantId>
{
    private Tenant()
    {
        // Para EF Core — inicialização via reflexão.
        // null! é justificado: EF Core popula essas propriedades via reflexão após instanciar.
        RazaoSocial = string.Empty;
        Cnpj = null!;
        ConfiguracaoFiscal = null!;
        Endereco = null!;
    }

    private Tenant(
        TenantId id,
        ClienteAppId clienteAppId,
        Cnpj cnpj,
        string razaoSocial,
        string? nomeFantasia,
        ConfiguracaoFiscal configuracaoFiscal,
        Endereco endereco,
        DateTime createdAt) : base(id)
    {
        ClienteAppId = clienteAppId;
        Cnpj = cnpj;
        RazaoSocial = razaoSocial;
        NomeFantasia = nomeFantasia;
        ConfiguracaoFiscal = configuracaoFiscal;
        Endereco = endereco;
        IsActive = true;
        CreatedAt = createdAt;
    }

    public ClienteAppId ClienteAppId { get; private set; }
    public Cnpj Cnpj { get; private set; }
    public string RazaoSocial { get; private set; }
    public string? NomeFantasia { get; private set; }
    public ConfiguracaoFiscal ConfiguracaoFiscal { get; private set; }
    public Endereco Endereco { get; private set; }
    public byte[]? Csc { get; private set; }
    public string? CIdToken { get; private set; }
    public byte[]? CertificadoPfxCriptografado { get; private set; }
    public byte[]? CertificadoSenhaCriptografada { get; private set; }
    public DateTime? CertificadoVencimento { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public static Result<Tenant> Criar(
        ClienteAppId clienteAppId,
        Cnpj cnpj,
        string razaoSocial,
        string? nomeFantasia,
        ConfiguracaoFiscal configuracaoFiscal,
        Endereco endereco,
        TimeProvider timeProvider)
    {
        if (string.IsNullOrWhiteSpace(razaoSocial))
            return Result.Failure<Tenant>(TenantErrors.RazaoSocialInvalida);

        var tenant = new Tenant(
            TenantId.New(),
            clienteAppId,
            cnpj,
            razaoSocial.Trim(),
            nomeFantasia?.Trim(),
            configuracaoFiscal,
            endereco,
            timeProvider.GetUtcNow().UtcDateTime);

        tenant.AddDomainEvent(new TenantProvisionadoEvent(
            tenant.Id,
            clienteAppId,
            cnpj.Valor));

        return Result.Success(tenant);
    }

    public void AtualizarCertificado(
        byte[] pfxCriptografado,
        byte[] senhaCriptografada,
        DateTime vencimento)
    {
        CertificadoPfxCriptografado = pfxCriptografado;
        CertificadoSenhaCriptografada = senhaCriptografada;
        CertificadoVencimento = vencimento;
    }

    public Result AtualizarCsc(byte[] cscCriptografado, string cIdToken)
    {
        if (cscCriptografado is null || cscCriptografado.Length == 0)
            return Result.Failure(TenantErrors.CscInvalido);

        // cIdToken: exatamente 6 dígitos numéricos conforme NT 2019.001 v1.50
        if (string.IsNullOrWhiteSpace(cIdToken) || cIdToken.Length != 6 || !cIdToken.All(char.IsDigit))
            return Result.Failure(TenantErrors.CIdTokenInvalido);

        Csc = cscCriptografado;
        CIdToken = cIdToken;
        return Result.Success();
    }

    public void Desativar() => IsActive = false;
}
