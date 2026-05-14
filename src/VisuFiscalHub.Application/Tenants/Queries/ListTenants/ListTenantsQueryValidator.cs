using FluentValidation;

namespace VisuFiscalHub.Application.Tenants.Queries.ListTenants;

public sealed class ListTenantsQueryValidator : AbstractValidator<ListTenantsQuery>
{
    public ListTenantsQueryValidator()
    {
        RuleFor(x => x.ClienteAppId)
            .NotEmpty().WithMessage("ClienteAppId é obrigatório");

        RuleFor(x => x.Page)
            .GreaterThan(0).WithMessage("Page deve ser maior que 0");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100).WithMessage("PageSize deve ser entre 1 e 100");
    }
}
