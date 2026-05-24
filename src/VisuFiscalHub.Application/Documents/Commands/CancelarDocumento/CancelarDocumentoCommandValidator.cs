using FluentValidation;

namespace VisuFiscalHub.Application.Documents.Commands.CancelarDocumento;

public sealed class CancelarDocumentoCommandValidator : AbstractValidator<CancelarDocumentoCommand>
{
    public CancelarDocumentoCommandValidator()
    {
        RuleFor(x => x.Justificativa)
            .NotEmpty()
            .MinimumLength(15)
            .MaximumLength(255);
    }
}
