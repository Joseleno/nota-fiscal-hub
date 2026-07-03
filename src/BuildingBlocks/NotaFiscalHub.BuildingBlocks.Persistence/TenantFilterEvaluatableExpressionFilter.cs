using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;

namespace NotaFiscalHub.BuildingBlocks.Persistence;

/// <summary>
/// Impede que o EF Core pré-avalie (funcletize) o acesso a <see cref="ITenantContext.ContaId"/> dentro
/// do query filter global de tenant. Por padrão, o EF trata sub-árvores "constantes" da expressão (que não
/// dependem de parâmetros da query) como candidatas a avaliação antecipada via compilação/invocação da
/// sub-árvore isolada — e qualquer exceção lançada nessa avaliação isolada é envolvida em
/// <see cref="InvalidOperationException"/>, mascarando <see cref="TenantNaoResolvidoException"/>.
///
/// Marcando esse acesso como "não avaliável", o EF mantém a chamada como parte da árvore de expressão
/// normal, e a exceção de fail-closed sobe sem wrapper na avaliação real da query.
///
/// Implementa <see cref="IEvaluatableExpressionFilter"/> diretamente (não a base relacional) para
/// funcionar tanto com o provider EF InMemory (usado nos testes de unidade) quanto com Npgsql.
/// </summary>
public sealed class TenantFilterEvaluatableExpressionFilter(EvaluatableExpressionFilterDependencies dependencies)
    : EvaluatableExpressionFilter(dependencies)
{
    public override bool IsEvaluatableExpression(Expression expression, IModel model)
    {
        if (expression is MemberExpression { Member.Name: nameof(ITenantContext.ContaId) } memberExpression
            && typeof(ITenantContext).IsAssignableFrom(memberExpression.Member.DeclaringType))
        {
            return false;
        }

        return base.IsEvaluatableExpression(expression, model);
    }
}
