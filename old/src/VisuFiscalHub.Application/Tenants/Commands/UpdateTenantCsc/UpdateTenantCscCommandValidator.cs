using FluentValidation;

namespace VisuFiscalHub.Application.Tenants.Commands.UpdateTenantCsc;

public sealed class UpdateTenantCscCommandValidator
    : AbstractValidator<UpdateTenantCscCommand>
{
    public UpdateTenantCscCommandValidator()
    {
        RuleFor(x => x.TenantId)
            .NotEmpty();

        RuleFor(x => x.ClienteAppId)
            .NotEmpty();

        RuleFor(x => x.Csc)
            .NotEmpty()
            .MinimumLength(8).WithMessage("CSC deve ter entre 8 e 36 caracteres")
            .MaximumLength(36).WithMessage("CSC deve ter entre 8 e 36 caracteres");

        RuleFor(x => x.CIdToken)
            .NotEmpty()
            .Matches(@"^[0-9]{6}$").WithMessage("cIdToken deve ter exatamente 6 dígitos");
    }
}
