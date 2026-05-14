using FluentValidation;

namespace VisuFiscalHub.Application.Tenants.Commands.UpdateTenantCertificate;

public sealed class UpdateTenantCertificateCommandValidator
    : AbstractValidator<UpdateTenantCertificateCommand>
{
    public UpdateTenantCertificateCommandValidator(TimeProvider timeProvider)
    {
        RuleFor(x => x.TenantId)
            .NotEmpty();

        RuleFor(x => x.ClienteAppId)
            .NotEmpty();

        RuleFor(x => x.PfxBytes)
            .NotNull()
            .Must(b => b.Length > 0 && b.Length <= 51_200)
            .WithMessage("PFX deve ter entre 1 byte e 50KB");

        RuleFor(x => x.Senha)
            .NotEmpty();

        RuleFor(x => x.Vencimento)
            .Must(v => v > timeProvider.GetUtcNow())
            .WithMessage("Data de vencimento deve ser futura");
    }
}
