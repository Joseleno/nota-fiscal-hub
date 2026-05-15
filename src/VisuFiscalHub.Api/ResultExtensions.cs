using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VisuFiscalHub.Domain.Common;

namespace VisuFiscalHub.Api;

public static class ResultExtensions
{
    public static IResult ToHttpResult(this Result result)
    {
        if (result.IsSuccess)
            return TypedResults.NoContent();

        return MapErrorToResult(result.Error);
    }

    public static IResult ToHttpResult<T>(this Result<T> result)
    {
        if (result.IsSuccess)
            return TypedResults.Ok(result.Value);

        return MapErrorToResult(result.Error);
    }

    public static IResult ToHttpResult<T>(
        this Result<T> result,
        Func<T, IResult> onSuccess)
    {
        if (result.IsSuccess)
            return onSuccess(result.Value);

        return MapErrorToResult(result.Error);
    }

    private static IResult MapErrorToResult(Error error)
    {
        var code = error.Code;

        if (code.EndsWith("NaoEncontrado"))
            return TypedResults.NotFound(new ProblemDetails
            {
                Title = error.Message,
                Detail = error.Code
            });

        if (code.EndsWith("NaoPertenceAoClienteApp") || code.EndsWith("Inativo"))
            return TypedResults.Problem(
                detail: error.Code,
                title: error.Message,
                statusCode: StatusCodes.Status403Forbidden);

        if (code.EndsWith("IdempotencyKeyJaUsada")
            || code.EndsWith("ClientIdJaExiste")
            || code.EndsWith("CnpjJaCadastrado"))
            return TypedResults.Conflict(new ProblemDetails
            {
                Title = error.Message,
                Detail = error.Code
            });

        if (code.EndsWith("TransicaoInvalida")
            || code.EndsWith("Validacao")
            || code.EndsWith("CnpjInvalido")
            || code.EndsWith("Failed")
            || code.EndsWith("PrazoDeCancelamentoExpirado")
            || code.EndsWith("SemItens")
            || code.EndsWith("ChaveAcessoInvalida")
            || code.EndsWith("ProdutoInvalido")
            || code.EndsWith("TributoInvalido")
            || code.EndsWith("CertificadoVencido")
            || code.EndsWith("CertificadoInvalido")
            || code.EndsWith("CscInvalido")
            || code.EndsWith("CIdTokenInvalido")
            || code.EndsWith("EnderecoInvalido")
            || code.EndsWith("ConfiguracaoFiscalInvalida")
            || code.EndsWith("RazaoSocialInvalida")
            || code.EndsWith("NomeInvalido")
            || code.EndsWith("ClientIdInvalido")
            || code.EndsWith("ClientSecretHashInvalido")
            || code.EndsWith("WebhookSecretInvalido")
            || code.EndsWith("StatusInvalidoParaOperacao")
            || code.EndsWith("TotalPagamentosInvalido")
            || code.EndsWith("ValorTotalInvalido")
            || code.EndsWith("CpfInvalido")
            || code.EndsWith("QrCodeInvalido")
            || code.EndsWith("PagamentoInvalido")
            || code.EndsWith("IdempotencyKeyInvalida")
            || code.EndsWith("SemCertificado")
            || code.EndsWith("TenantInvalido")
            || code.EndsWith("ClienteAppInvalido"))
            return TypedResults.UnprocessableEntity(new ProblemDetails
            {
                Title = error.Message,
                Detail = error.Code
            });

        return TypedResults.Problem(
            detail: error.Code,
            title: error.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
}
