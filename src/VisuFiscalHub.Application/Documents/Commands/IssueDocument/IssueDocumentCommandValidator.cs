using FluentValidation;
using VisuFiscalHub.Domain.Enums;

namespace VisuFiscalHub.Application.Documents.Commands.IssueDocument;

internal sealed class IssueDocumentCommandValidator : AbstractValidator<IssueDocumentCommand>
{
    private static readonly int[] IndPresencaValidosNfce = [1, 3, 4, 9];
    private static readonly int[] IndPresencaValidosNfe  = [0, 1, 2, 3, 4, 5, 9];

    public IssueDocumentCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.TenantId).NotEqual(default(Domain.Identifiers.TenantId));
        RuleFor(x => x.ClienteAppId).NotEqual(default(Domain.Identifiers.ClienteAppId));

        RuleFor(x => x.Itens)
            .NotEmpty().WithMessage("A lista de itens não pode ser vazia.");

        RuleFor(x => x.Pagamentos)
            .NotEmpty().WithMessage("A lista de pagamentos não pode ser vazia.");

        RuleFor(x => x.IndPresenca)
            .Must((cmd, v) =>
            {
                var validos = cmd.Tipo == TipoDocumento.NFe ? IndPresencaValidosNfe : IndPresencaValidosNfce;
                return validos.Contains(v);
            })
            .WithMessage("IndPresenca inválido para o tipo de documento.");

        RuleForEach(x => x.Itens).ChildRules(item =>
        {
            item.RuleFor(i => i.Ncm)
                .NotEmpty()
                .Matches(@"^\d{8}$").WithMessage("NCM deve ter exatamente 8 dígitos.");

            item.RuleFor(i => i.Quantidade).GreaterThan(0);
            item.RuleFor(i => i.ValorUnitario).GreaterThan(0);
            item.RuleFor(i => i.ValorDesconto).GreaterThanOrEqualTo(0);
        });

        RuleForEach(x => x.Pagamentos).ChildRules(pag =>
        {
            pag.RuleFor(p => p.Valor).GreaterThan(0);
        });

        // Total de pagamentos deve fechar com o total dos itens (tolerância R$ 0,01).
        // Itens vazios são capturados pelo NotEmpty acima — o guard protege apenas o Sum.
        RuleFor(x => x)
            .Must(cmd =>
            {
                if (cmd.Itens.Count == 0) return true;
                var totalItens = cmd.Itens.Sum(i => (i.Quantidade * i.ValorUnitario) - i.ValorDesconto);
                var totalPagamentos = cmd.Pagamentos.Sum(p => p.Valor);
                return Math.Abs(totalItens - totalPagamentos) <= 0.01m;
            })
            .WithMessage("Total dos pagamentos não fecha com o total dos itens (tolerância R$ 0,01).");

        // CPF obrigatório se valor total > R$ 10.000,00 (condição estrita >, não >=)
        RuleFor(x => x)
            .Must(cmd =>
            {
                if (cmd.Itens.Count == 0) return true;
                var totalItens = cmd.Itens.Sum(i => (i.Quantidade * i.ValorUnitario) - i.ValorDesconto);
                if (totalItens > 10_000m)
                    return cmd.Consumidor?.Cpf is not null;
                return true;
            })
            .WithMessage("CPF do consumidor é obrigatório quando o valor total excede R$ 10.000,00.");

        When(x => x.Tipo == TipoDocumento.NFe, () =>
        {
            RuleFor(x => x.NfeDestinatario)
                .NotNull().WithMessage("Destinatário é obrigatório para NF-e.");

            RuleFor(x => x.NatOp)
                .NotEmpty().WithMessage("Natureza da operação é obrigatória para NF-e.")
                .MaximumLength(60).WithMessage("Natureza da operação não pode exceder 60 caracteres.");
        });

        When(x => x.Tipo == TipoDocumento.NfCe, () =>
        {
            RuleFor(x => x.NfeDestinatario)
                .Null().WithMessage("Destinatário NF-e não é permitido em NFC-e Modelo 65.");
        });

        When(x => x.Tipo == TipoDocumento.NFSe, () =>
        {
            RuleFor(x => x.Tomador).NotNull().WithMessage("Tomador é obrigatório para NFS-e.");
            RuleFor(x => x.ServicoNfse).NotNull().WithMessage("ServicoNfse é obrigatório para NFS-e.");
            RuleFor(x => x.NfeDestinatario).Null().WithMessage("NfeDestinatario não é permitido para NFS-e.");
        });
    }
}
