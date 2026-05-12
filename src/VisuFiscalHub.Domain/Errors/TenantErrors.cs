using VisuFiscalHub.Domain.Common;

namespace VisuFiscalHub.Domain.Errors;

public static class TenantErrors
{
    public static readonly Error NaoEncontrado =
        new("Tenant.NaoEncontrado", "Tenant não encontrado.");

    public static readonly Error CnpjInvalido =
        new("Tenant.CnpjInvalido", "O CNPJ informado é inválido.");

    public static readonly Error CnpjJaCadastrado =
        new("Tenant.CnpjJaCadastrado", "Já existe um Tenant com este CNPJ neste ClienteApp.");

    public static readonly Error NaoPertenceAoClienteApp =
        new("Tenant.NaoPertenceAoClienteApp", "O Tenant não pertence ao ClienteApp autenticado.");

    public static readonly Error SemCertificado =
        new("Tenant.SemCertificado", "O Tenant não possui certificado digital configurado.");

    public static readonly Error Inativo =
        new("Tenant.Inativo", "O Tenant está inativo.");

    public static readonly Error EnderecoInvalido =
        new("Tenant.EnderecoInvalido", "O endereço do Tenant é inválido.");

    public static readonly Error ConfiguracaoFiscalInvalida =
        new("Tenant.ConfiguracaoFiscalInvalida", "A configuração fiscal do Tenant é inválida.");

    public static readonly Error RazaoSocialInvalida =
        new("Tenant.RazaoSocialInvalida", "A razão social do Tenant é inválida.");

    public static readonly Error CscInvalido =
        new("Tenant.CscInvalido", "O CSC informado é inválido.");

    public static readonly Error CIdTokenInvalido =
        new("Tenant.CIdTokenInvalido", "O cIdToken deve ter exatamente 6 dígitos numéricos.");
}
