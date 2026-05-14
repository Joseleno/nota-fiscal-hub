using FluentValidation;

namespace VisuFiscalHub.Application.Tenants.Queries.GetCertificadoStatus;

public sealed class GetCertificadoStatusQueryValidator : AbstractValidator<GetCertificadoStatusQuery>
{
    public GetCertificadoStatusQueryValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty().WithMessage("TenantId é obrigatório");
        RuleFor(x => x.ClienteAppId).NotEmpty().WithMessage("ClienteAppId é obrigatório");
    }
}
