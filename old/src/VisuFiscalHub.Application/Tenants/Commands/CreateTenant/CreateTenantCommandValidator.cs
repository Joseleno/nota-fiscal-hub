using FluentValidation;
using VisuFiscalHub.Domain.Enums;

namespace VisuFiscalHub.Application.Tenants.Commands.CreateTenant;

public sealed class CreateTenantCommandValidator
    : AbstractValidator<CreateTenantCommand>
{
    public CreateTenantCommandValidator()
    {
        RuleFor(x => x.ClienteAppId)
            .NotEmpty().WithMessage("ClienteAppId é obrigatório");

        RuleFor(x => x.Cnpj)
            .NotEmpty().WithMessage("CNPJ é obrigatório")
            .Matches(@"^[\d.\-/]{14,18}$").WithMessage("CNPJ inválido");

        RuleFor(x => x.RazaoSocial)
            .NotEmpty()
            .MaximumLength(300);

        RuleFor(x => x.RegimeTributario)
            .IsInEnum().WithMessage("Regime tributário inválido");

        RuleFor(x => x.Ambiente)
            .IsInEnum().WithMessage("Ambiente SEFAZ inválido (1=Produção, 2=Homologação)");

        RuleFor(x => x.UfCodigo)
            .InclusiveBetween(11, 53).WithMessage("Código IBGE da UF inválido");

        RuleFor(x => x.Serie)
            .NotEmpty()
            .Matches(@"^[0-9]{1,3}$").WithMessage("Série deve conter apenas dígitos (1 a 3 caracteres)");

        RuleFor(x => x.Endereco)
            .NotNull()
            .ChildRules(e =>
            {
                e.RuleFor(x => x.Logradouro).NotEmpty();
                e.RuleFor(x => x.Numero).NotEmpty();
                e.RuleFor(x => x.Bairro).NotEmpty();
                e.RuleFor(x => x.Municipio).NotEmpty();
                e.RuleFor(x => x.CodigoMunicipio).InclusiveBetween(1_000_000, 9_999_999)
                    .WithMessage("Código IBGE do município deve ter 7 dígitos (1000000–9999999)");
                e.RuleFor(x => x.Uf).NotEmpty().Length(2);
                e.RuleFor(x => x.Cep)
                    .NotEmpty()
                    .Matches(@"^[0-9]{8}$").WithMessage("CEP deve ter 8 dígitos sem máscara");
            });
    }
}
