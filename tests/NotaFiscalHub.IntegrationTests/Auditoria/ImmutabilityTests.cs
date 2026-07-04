using Microsoft.EntityFrameworkCore;
using Npgsql;
using NotaFiscalHub.BuildingBlocks.Auditoria;
using Xunit;

namespace NotaFiscalHub.IntegrationTests.Auditoria;

/// <summary>
/// Critério de aceite 3 (spec B5): <c>UPDATE</c>/<c>DELETE</c> diretos via SQL na tabela lançam
/// <see cref="PostgresException"/> (trigger de bloqueio), mesmo por superusuário de aplicação — a garantia
/// real enquanto o role dedicado <c>nfh_app</c> não existir em nenhum ambiente (spec B5 Assunção A2).
///
/// Diferente das demais classes desta pasta, usa <see cref="AuditoriaTestFixture.AplicarMigrationsReaisAsync"/>
/// (MIGRATIONS reais, não <c>EnsureCreatedAsync</c>) — o trigger só existe via
/// <c>migrationBuilder.Sql(...)</c> na migration <c>InitialCreate</c>, nunca via <c>EnsureCreatedAsync</c>
/// (que só materializa o model, ignorando qualquer <c>Sql(...)</c> registrado na migration).
///
/// NÃO roda neste ambiente (Docker Desktop indisponível) — ver comentário de classe de
/// <see cref="AuditoriaTestFixture"/>. Esta é a suíte que MAIS se beneficiaria de rodar contra Postgres
/// real assim que possível: <see cref="MigrationSqlTests"/> (unit test) prova só que o texto do trigger
/// está correto/presente na migration — só este teste prova que o trigger de fato bloqueia em runtime.
/// </summary>
public class ImmutabilityTests : IAsyncLifetime
{
    private readonly AuditoriaTestFixture _fixture = new();
    private Guid _registroId;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        await _fixture.AplicarMigrationsReaisAsync();
        _registroId = await SemearRegistroAsync();
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task UpdateDireto_LancaPostgresException()
    {
        await using var db = _fixture.NovoAuditoriaDbContext();

        await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync(
                "UPDATE auditoria.registro_auditoria SET ator = 'hack' WHERE id = {0}", _registroId));
    }

    [Fact]
    public async Task DeleteDireto_LancaPostgresException()
    {
        await using var db = _fixture.NovoAuditoriaDbContext();

        await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync(
                "DELETE FROM auditoria.registro_auditoria WHERE id = {0}", _registroId));
    }

    private async Task<Guid> SemearRegistroAsync()
    {
        await using var db = _fixture.NovoAuditoriaDbContext();
        var registro = new RegistroAuditoriaEntity
        {
            Id = Guid.NewGuid(),
            ContaId = Guid.NewGuid(),
            TipoEvento = "ApiKeyRevogada.v1",
            Acao = "apikey.revogada",
            RecursoTipo = "ApiKey",
            RecursoId = Guid.NewGuid().ToString(),
            Ator = "nfh_live_ab12",
            OcorridoEm = DateTimeOffset.UtcNow,
            RegistradoEm = DateTimeOffset.UtcNow,
            CorrelationId = Guid.NewGuid().ToString("N"),
            MessageId = Guid.NewGuid(),
            Dados = null,
        };
        db.Add(registro);
        await db.SaveChangesAsync();
        return registro.Id;
    }
}
