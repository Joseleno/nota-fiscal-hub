using Microsoft.EntityFrameworkCore;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Interfaces;

namespace VisuFiscalHub.Infrastructure.Persistence;

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;

    public UnitOfWork(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => _context.SaveChangesAsync(ct);

    public async Task<Result> ExecuteInTransactionAsync(
        Func<CancellationToken, Task<Result>> operation,
        CancellationToken ct = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            var result = await operation(ct);
            if (result.IsFailure)
            {
                await transaction.RollbackAsync(ct);
                return result;
            }

            await transaction.CommitAsync(ct);
            return Result.Success();
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }
}
