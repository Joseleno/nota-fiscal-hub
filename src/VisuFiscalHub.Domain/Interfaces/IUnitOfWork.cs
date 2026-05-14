using VisuFiscalHub.Domain.Common;

namespace VisuFiscalHub.Domain.Interfaces;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Executa <paramref name="operation"/> dentro de uma transação de banco de dados.
    /// Faz commit se a operação retornar sucesso; faz rollback caso contrário ou em exceção.
    /// </summary>
    Task<Result> ExecuteInTransactionAsync(
        Func<CancellationToken, Task<Result>> operation,
        CancellationToken ct = default);
}
