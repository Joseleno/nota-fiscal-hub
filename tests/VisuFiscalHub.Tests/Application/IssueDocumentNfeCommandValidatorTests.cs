using FluentValidation.TestHelper;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Application.Documents.Commands.IssueDocument;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Tests.Application;

public class IssueDocumentNfeCommandValidatorTests
{
    private readonly IssueDocumentCommandValidator _validator = new();

    private static NfeDestinatarioDto ValidDest() => new(
        CnpjOuCpf: "11222333000181",
        RazaoSocial: "Empresa Teste Ltda",
        IndIeDest: 9, Ie: null,
        Logradouro: "Rua Teste", Numero: "100", Complemento: null,
        Bairro: "Centro", Municipio: "São Paulo",
        CodigoMunicipio: "3550308", Uf: "SP", Cep: "01310100",
        Email: null);

    private static IssueDocumentCommand BaseNfeCommand() => new()
    {
        TenantId = new TenantId(Guid.NewGuid()),
        ClienteAppId = new ClienteAppId(Guid.NewGuid()),
        IdempotencyKey = "KEY-001",
        Tipo = TipoDocumento.NFe,
        IndPresenca = 0,
        NatOp = "Venda de mercadoria",
        NfeDestinatario = ValidDest(),
        Itens =
        [
            new ItemDocumentoDto(
                CodigoProduto: "001",
                Descricao: "Produto",
                Ncm: "12345678",
                Cest: null,
                CfopSaida: "5102",
                UnidadeComercial: "UN",
                Quantidade: 1m,
                ValorUnitario: 10m,
                ValorDesconto: 0m,
                OrigemMercadoria: OrigemMercadoria.Nacional,
                Tributo: new TributoDto(
                    TipoIcms: TipoIcms.CSOSN,
                    CsosnOuCst: 102,
                    AliquotaIcms: 0m,
                    BaseCalculoIcms: 0m,
                    ValorIcms: 0m,
                    CstPis: CstPisCofins.Cst07,
                    BaseCalculoPis: 0m,
                    AliquotaPis: 0m,
                    ValorPis: 0m,
                    CstCofins: CstPisCofins.Cst07,
                    BaseCalculoCofins: 0m,
                    AliquotaCofins: 0m,
                    ValorCofins: 0m))
        ],
        Pagamentos = [new PagamentoDto(TipoPagamento.Dinheiro, 10m)]
    };

    [Fact]
    public void Nfe_ComDestinatarioValido_Valido()
    {
        var result = _validator.TestValidate(BaseNfeCommand());
        result.ShouldNotHaveValidationErrorFor(x => x.NfeDestinatario);
    }

    [Fact]
    public void Nfe_SemDestinatario_Invalido()
    {
        var cmd = BaseNfeCommand() with { NfeDestinatario = null };
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.NfeDestinatario);
    }

    [Fact]
    public void Nfe_SemNatOp_Invalido()
    {
        var cmd = BaseNfeCommand() with { NatOp = null };
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.NatOp);
    }

    [Fact]
    public void Nfce_ComDestinatario_Invalido()
    {
        var cmd = new IssueDocumentCommand
        {
            TenantId = new TenantId(Guid.NewGuid()),
            ClienteAppId = new ClienteAppId(Guid.NewGuid()),
            IdempotencyKey = "KEY-002",
            Tipo = TipoDocumento.NfCe,
            IndPresenca = 1,
            NfeDestinatario = ValidDest(),
            Itens = BaseNfeCommand().Itens,
            Pagamentos = BaseNfeCommand().Pagamentos
        };
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.NfeDestinatario);
    }

    [Fact]
    public void Nfe_IndPresenca0_Valido()
    {
        var cmd = BaseNfeCommand() with { IndPresenca = 0 };
        var result = _validator.TestValidate(cmd);
        result.ShouldNotHaveValidationErrorFor(x => x.IndPresenca);
    }
}
