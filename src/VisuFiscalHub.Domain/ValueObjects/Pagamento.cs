using System.Collections.Generic;
using System.Linq;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Errors;

namespace VisuFiscalHub.Domain.ValueObjects;

public sealed record Pagamento(TipoPagamento TipoPagamento, decimal Valor)
{
    public static Result<Pagamento> Criar(TipoPagamento tipoPagamento, decimal valor)
    {
        if (valor <= 0)
            return Result.Failure<Pagamento>(DocumentoFiscalErrors.PagamentoInvalido);

        return Result.Success(new Pagamento(tipoPagamento, valor));
    }

    public static Result ValidarTotalPagamentos(
        IEnumerable<Pagamento> pagamentos,
        decimal valorTotalNota)
    {
        var totalPagamentos = pagamentos.Sum(p => p.Valor);
        var diferenca = Math.Abs(totalPagamentos - valorTotalNota);

        // Tolerância de R$ 0,01
        if (diferenca > 0.01m)
            return Result.Failure(DocumentoFiscalErrors.TotalPagamentosInvalido);

        return Result.Success();
    }
}
