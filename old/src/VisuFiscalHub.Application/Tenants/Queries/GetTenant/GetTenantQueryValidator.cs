using FluentValidation;

namespace VisuFiscalHub.Application.Tenants.Queries.GetTenant;

public sealed class GetTenantQueryValidator : AbstractValidator<GetTenantQuery>
{
    public GetTenantQueryValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty().WithMessage("TenantId é obrigatório");
        RuleFor(x => x.ClienteAppId).NotEmpty().WithMessage("ClienteAppId é obrigatório");
    }
}
