using Microsoft.EntityFrameworkCore;
using NotaFiscalHub.BuildingBlocks.Auditoria;
using NotaFiscalHub.BuildingBlocks.Idempotency;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;
using NotaFiscalHub.BuildingBlocks.Messaging;
using NotaFiscalHub.BuildingBlocks.Persistence;
using NotaFiscalHub.Modules.ContasPlanos.Infrastructure;
using NotaFiscalHub.Modules.Documentos.Infrastructure;
using NotaFiscalHub.Modules.Emissao.Infrastructure;
using NotaFiscalHub.Modules.EmpresasCertificados.Infrastructure;
using Xunit;

namespace NotaFiscalHub.ArchitectureTests;

/// <summary>
/// Garante que toda entidade mapeada nos DbContexts de módulo é tenant-scoped (implementa
/// <see cref="ITenantScopedEntity"/>) ou está explicitamente na allowlist versionada abaixo — nunca
/// silenciosamente isenta. Cada item da allowlist EXIGE justificativa em comentário; crescimento
/// silencioso da allowlist anula a garantia de isolamento (spec B2, "Riscos").
///
/// Allowlist atual: "Plano" (catálogo global, sem dono por conta — ainda não existe no código, candidato
/// da spec B2). <see cref="OutboxMessage"/>/<see cref="InboxMessage"/> (Tarefa 3, spec B3): o dispatcher
/// precisa fazer polling entre tenants (uma mensagem "Pendente" de qualquer conta deve ser visível ao
/// <c>SELECT ... FOR UPDATE SKIP LOCKED</c>) — por isso <c>ContaId</c> é uma coluna simples (nullable,
/// para eventos de plataforma) e NÃO um filtro global de tenant. O isolamento por tenant dessas linhas é
/// aplicado no nível de negócio pelo próprio dispatcher (<c>BeginTenantScope(ContaId)</c> antes de invocar
/// o handler), não pelo query filter — ver regra de escopo de tenant no dispatch (spec B3 passo 4).
///
/// <c>IdempotencyRecordEntity</c> (Tarefa 4, spec B4) NÃO está na allowlist: implementa
/// <see cref="ITenantScopedEntity"/> com <c>ContaId</c> não anulável, então recebe o filtro "Tenant"
/// normalmente como qualquer entidade de módulo — o job de expiração
/// (<c>IdempotencyExpirationJob</c>) contorna esse filtro pontualmente com
/// <c>IgnoreQueryFilters(["Tenant"])</c> dentro de um <c>BeginSystemScope</c> explícito (uso restrito ao
/// kernel, ver <see cref="IgnoreQueryFilters_ProibidoForaDoKernel"/>), em vez de a entidade ficar isenta do
/// filtro por padrão.
///
/// <c>RegistroAuditoriaEntity</c> (Tarefa 5, spec B5) ESTÁ na allowlist, pela mesma classe de exceção de
/// <c>OutboxMessage</c>/<c>InboxMessage</c> — mas por um motivo de NEGÓCIO, não só técnico: registros de
/// auditoria de eventos de ESCOPO DE SISTEMA (ações administrativas/backoffice sem tenant, ex.:
/// <c>ContaSuspensa</c> disparada pelo sistema) são gravados deliberadamente com <c>ContaId == null</c>
/// sob <c>BeginSystemScope</c> — um recurso exigido pela spec B5 (§Abordagem passo 6/Assunção A4), não um
/// acidente de implementação. <c>RegistroAuditoriaEntity.ContaId</c> é <c>Guid?</c> (anulável) — não
/// implementa <see cref="ITenantScopedEntity"/> (cujo contrato exige <c>Guid</c> não anulável) e por isso
/// <c>AuditoriaDbContext</c> estende <c>ModuleDbContext</c> diretamente, não <c>TenantDbContext</c> (ver
/// comentário de classe de ambos). O isolamento por tenant destas linhas é aplicado EXCLUSIVAMENTE no nível
/// de aplicação/consulta: <c>ConsultaAuditoria</c> (que implementa <c>IConsultaAuditoria</c>, cujo
/// <c>FiltroAuditoria.ContaId</c> é obrigatório/não anulável) aplica <c>WHERE conta_id = @contaId</c>
/// incondicionalmente e portanto NUNCA retorna registros de escopo de sistema (<c>ContaId == null</c>) nem
/// de outro tenant — não há filtro global de EF Core aqui como rede de segurança adicional (ver
/// <see cref="TenantScoped_ExigeContaId"/> abaixo, que também não varre <c>AuditoriaDbContext</c>, já que
/// ele deliberadamente não aparece na lista de DbContexts varrida por este teste — não é um
/// <c>TenantDbContext</c>).
/// </summary>
public class TenantScopedEntityTests
{
    private static readonly HashSet<Type> AllowlistEntidadesGlobais =
        [typeof(OutboxMessage), typeof(InboxMessage), typeof(RegistroAuditoriaEntity)];

    [Fact]
    public void TenantScoped_ExigeContaId()
    {
        var ofensores = TodosOsDbContexts()
            .SelectMany(ctx => ctx.Model.GetEntityTypes())
            .Where(et => !typeof(ITenantScopedEntity).IsAssignableFrom(et.ClrType))
            .Where(et => !AllowlistEntidadesGlobais.Contains(et.ClrType))
            .Select(et => et.ClrType.Name)
            .ToList();

        Assert.Empty(ofensores);
    }

    [Fact]
    public void TenantScoped_TemQueryFilterRegistradoEColunaContaIdNaoNula()
    {
        var entidadesTenantScoped = TodosOsDbContexts()
            .SelectMany(ctx => ctx.Model.GetEntityTypes())
            .Where(et => typeof(ITenantScopedEntity).IsAssignableFrom(et.ClrType))
            .ToList();

        foreach (var entidade in entidadesTenantScoped)
        {
            Assert.True(entidade.GetDeclaredQueryFilters().Any(f => f.Key == "Tenant"),
                $"{entidade.ClrType.Name} não tem o query filter nomeado \"Tenant\" registrado.");

            var contaIdProperty = entidade.FindProperty(nameof(ITenantScopedEntity.ContaId));
            Assert.NotNull(contaIdProperty);
            Assert.False(contaIdProperty.IsNullable,
                $"{entidade.ClrType.Name}.ContaId deve ser NOT NULL.");
        }
    }

    [Fact]
    public void TodoDbContextConcreto_HerdaDeTenantDbContext()
    {
        Type[] dbContextsDeModulo =
        [
            typeof(ContasPlanosDbContext),
            typeof(EmpresasCertificadosDbContext),
            typeof(EmissaoDbContext),
            typeof(DocumentosDbContext),
            typeof(IdempotencyDbContext),
        ];

        var ofensores = dbContextsDeModulo.Where(t => !typeof(TenantDbContext).IsAssignableFrom(t)).ToList();

        Assert.Empty(ofensores);
    }

    [Fact]
    public void IgnoreQueryFilters_ProibidoForaDoKernel()
    {
        var raizSolution = EncontrarRaizDaSolution();
        var srcDir = Path.Combine(raizSolution, "src");

        var arquivosComIgnoreQueryFilters = Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}BuildingBlocks{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains("IgnoreQueryFilters"))
            .ToList();

        Assert.Empty(arquivosComIgnoreQueryFilters);
    }

    private static string EncontrarRaizDaSolution()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NotaFiscalHub.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("NotaFiscalHub.sln não encontrado a partir de " + AppContext.BaseDirectory);
    }

    private static IEnumerable<DbContext> TodosOsDbContexts()
    {
        var tenantContext = new AmbientTenantContext();
        using var _ = tenantContext.BeginSystemScope("architecture-test", "TenantScopedEntityTests");

        yield return NovoContexto<ContasPlanosDbContext>(tenantContext, (o, tc) => new ContasPlanosDbContext(o, tc));
        yield return NovoContexto<EmpresasCertificadosDbContext>(tenantContext, (o, tc) => new EmpresasCertificadosDbContext(o, tc));
        yield return NovoContexto<EmissaoDbContext>(tenantContext, (o, tc) => new EmissaoDbContext(o, tc));
        yield return NovoContexto<DocumentosDbContext>(tenantContext, (o, tc) => new DocumentosDbContext(o, tc));
        yield return NovoContexto<IdempotencyDbContext>(tenantContext, (o, tc) => new IdempotencyDbContext(o, tc));
    }

    private static TContext NovoContexto<TContext>(
        ITenantContext tenantContext, Func<DbContextOptions<TContext>, ITenantContext, TContext> factory)
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return factory(options, tenantContext);
    }
}
