using Microsoft.EntityFrameworkCore;
using Npgsql;
using VisuFiscalHub.Application.Common;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Errors;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Infrastructure.Persistence;

public sealed class SequenceManager : ISequenceManager
{
    private readonly ApplicationDbContext _context;

    public SequenceManager(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> EnsureNumeracaoSequenceAsync(TenantId tenantId, string serie, CancellationToken ct = default)
    {
        if (!FiscalConstants.SerieRegex.IsMatch(serie))
            return Result.Failure(TenantErrors.ConfiguracaoFiscalInvalida);

        var sequenceName = BuildSequenceName(tenantId, serie);

#pragma warning disable EF1002
        await _context.Database.ExecuteSqlRawAsync(
            $"CREATE SEQUENCE IF NOT EXISTS {sequenceName} START 1 INCREMENT 1",
            ct);
#pragma warning restore EF1002

        return Result.Success();
    }

    public async Task<Result<long>> GetNextNumeroAsync(TenantId tenantId, string serie, CancellationToken ct = default)
    {
        if (!FiscalConstants.SerieRegex.IsMatch(serie))
            return Result.Failure<long>(TenantErrors.ConfiguracaoFiscalInvalida);

        var sequenceName = BuildSequenceName(tenantId, serie);

        try
        {
#pragma warning disable EF1002
            var numero = await _context.Database
                .SqlQueryRaw<long>($"SELECT nextval('{sequenceName}')")
                .FirstAsync(ct);
#pragma warning restore EF1002

            return Result.Success(numero);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // Sequence não existe — tenant provisionado sem EnsureNumeracaoSequenceAsync
            return Result.Failure<long>(TenantErrors.ConfiguracaoFiscalInvalida);
        }
    }

    private static string BuildSequenceName(TenantId tenantId, string serie)
        => $"seq_nfe_{tenantId.Value.ToString("N")}_{serie}";
}
