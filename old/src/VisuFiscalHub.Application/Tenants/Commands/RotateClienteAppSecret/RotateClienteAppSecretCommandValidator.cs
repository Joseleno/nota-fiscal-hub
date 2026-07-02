using FluentValidation;

namespace VisuFiscalHub.Application.Tenants.Commands.RotateClienteAppSecret;

public sealed class RotateClienteAppSecretCommandValidator
    : AbstractValidator<RotateClienteAppSecretCommand>
{
    public RotateClienteAppSecretCommandValidator()
    {
        RuleFor(x => x.ClienteAppId)
            .NotEmpty().WithMessage("ClienteAppId é obrigatório");
    }
}
