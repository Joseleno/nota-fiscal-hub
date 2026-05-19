using FluentValidation;
using Shouldly;
using VisuFiscalHub.Application.Documents.Commands.IssueDocument;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Tests.Application;

public class IssueDocumentCommandValidatorTests
{
    private readonly IssueDocumentCommandValidator _validator = new();

    private static TributoDto TributoValido() => new(
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
        ValorCofins: 0m);

    private static IssueDocumentCommand CmdValido(
        decimal valorUnitario = 100m,
        string? cpf = null,
        string ncm = "12345678",
        int indPresenca = 1) => new()
    {
        TenantId = TenantId.New(),
        ClienteAppId = ClienteAppId.New(),
        IdempotencyKey = Guid.NewGuid().ToString(),
        Tipo = TipoDocumento.NfCe,
        Itens =
        [
            new ItemDocumentoDto(
                CodigoProduto: "001",
                Descricao: "Produto",
                Ncm: ncm,
                Cest: null,
                CfopSaida: "5102",
                UnidadeComercial: "UN",
                Quantidade: 1m,
                ValorUnitario: valorUnitario,
                ValorDesconto: 0m,
                OrigemMercadoria: OrigemMercadoria.Nacional,
                Tributo: TributoValido())
        ],
        Pagamentos = [new PagamentoDto(TipoPagamento.Dinheiro, valorUnitario)],
        Consumidor = cpf is not null ? new ConsumidorDto(cpf, null) : null,
        IndPresenca = indPresenca
    };

    [Fact]
    public void Validar_QuandoValorAcima10000SemCpf_DeveRejeitar()
    {
        var cmd = CmdValido(valorUnitario: 10_000.01m);
        var result = _validator.Validate(cmd);
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage.Contains("10.000"));
    }

    [Fact]
    public void Validar_QuandoValorExatamente10000SemCpf_DevePermitir()
    {
        var cmd = CmdValido(valorUnitario: 10_000m);
        var result = _validator.Validate(cmd);
        result.IsValid.ShouldBeTrue("R$10.000,00 exatos sem CPF deve ser permitido (condição é >, não >=)");
    }

    [Fact]
    public void Validar_QuandoValorAcima10000ComCpf_DevePermitir()
    {
        var cmd = CmdValido(valorUnitario: 10_000.01m, cpf: "12345678909");
        var result = _validator.Validate(cmd);
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validar_QuandoPagamentosNaoFechamTotal_DeveRejeitar()
    {
        var cmd = CmdValido(valorUnitario: 100m) with
        {
            Pagamentos = [new PagamentoDto(TipoPagamento.Dinheiro, 90m)]
        };
        var result = _validator.Validate(cmd);
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage.Contains("pagamentos"));
    }

    [Fact]
    public void Validar_QuandoNcmCom7Digitos_DeveRejeitar()
    {
        var cmd = CmdValido(ncm: "1234567");
        var result = _validator.Validate(cmd);
        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validar_QuandoNcmCom8Digitos_DeveAceitar()
    {
        var cmd = CmdValido(ncm: "12345678");
        var result = _validator.Validate(cmd);
        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(2)]
    public void Validar_QuandoIndPresencaProibido_DeveRejeitar(int indPresenca)
    {
        var cmd = CmdValido(indPresenca: indPresenca);
        var result = _validator.Validate(cmd);
        result.IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(9)]
    public void Validar_QuandoIndPresencaPermitido_DeveAceitar(int indPresenca)
    {
        var cmd = CmdValido(indPresenca: indPresenca);
        var result = _validator.Validate(cmd);
        result.IsValid.ShouldBeTrue();
    }
}
