using FluentValidation;

namespace VisuFiscalHub.Application.Documents.Commands.IssueDocument;

internal sealed class IssueDocumentCommandValidator : AbstractValidator<IssueDocumentCommand>
{
    private static readonly int[] IndPresencaValidos = [1, 3, 4, 9];

    public IssueDocumentCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.TenantId).NotEqual(default(Domain.Identifiers.TenantId));
        RuleFor(x => x.ClienteAppId).NotEqual(default(Domain.Identifiers.ClienteAppId));

        RuleFor(x => x.Itens)
            .NotEmpty().WithMessage("A lista de itens não pode ser vazia.");

        RuleFor(x => x.IndPresenca)
            .Must(v => IndPresencaValidos.Contains(v))
            .WithMessage("IndPresenca deve ser 1, 3, 4 ou 9. O valor 2 é explicitamente rejeitado.");

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

        // Total de pagamentos deve fechar com o total dos itens (tolerância R$ 0,01)
        RuleFor(x => x)
            .Must(cmd =>
            {
                if (cmd.Itens.Count == 0 || cmd.Pagamentos.Count == 0) return true;
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
    }
}
