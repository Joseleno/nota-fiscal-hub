# Spec B5 — Auditoria append-only por eventos de integração

> Card: https://app.clickup.com/t/86e24c1jh | Grupo B | Gerada em 2026-07-02 por time de agentes (escritor especialista + revisor adversarial)

## Objetivo

Entregar a fundação genérica da trilha de auditoria do kernel transversal (design §2.2 "Cross-cutting kernel"; plano-mestre Fase 0): uma tabela **append-only** alimentada exclusivamente por **eventos de integração** consumidos via inbox (biblioteca de B3), registrando quem/quê/quando/`conta_id`/`correlationId`, com imutabilidade garantida em duas camadas (aplicação + banco), sem PII/XML, consulta básica por contrato e política de retenção alinhada à guarda fiscal.

## Contexto e referências

- Design: `docs/superpowers/specs/2026-07-01-nota-fiscal-hub-produto-design.md` — §2.2 (kernel: "auditoria append-only (quem/quando/de onde/o quê/antes-depois) alimentada pelos eventos de integração, cobrindo ações administrativas sensíveis"; ver Assunção A4 sobre a cobertura parcial de "de onde" e "antes-depois" nesta fundação), §2.3 regras 2 (inbox idempotente), 5 (tenant como fronteira) e 6 (eventos carregam identificadores, nunca XML/PDF/PII), §4.1 (convenções: sem delete físico de dado fiscal).
- Plano-mestre: `docs/superpowers/plans/2026-07-01-nota-fiscal-hub-mvp-plano-mestre.md` — Global Constraints (TDD; handlers idempotentes testados com duplicata/fora-de-ordem; eventos sem PII; logs sem PII; `conta_id` + filtro global) e Fase 0 ("Auditoria append-only alimentada por eventos de integração").
- Decisões: `docs/decisions.md` §0 — D-2026-07-01-10 (todo schema precisa de módulo dono), D-2026-07-01-08 (destruição de A1/CSC no encerramento é **auditada** — consumidor futuro desta fundação).
- Handoff: `docs/superpowers/handoffs/2026-07-01-revisao-docs-e-gate0-handoff.md` — Gate 0 pendente de matriz final; ver "Assunções" abaixo.

## Escopo (dentro / fora)

**Dentro:**
- Schema `auditoria` com tabela `registro_auditoria` (dono: kernel/BuildingBlocks — ver Assunção A1).
- Handler inbox `AuditoriaEventHandler` (roda no Worker, sob `BeginTenantScope`) que materializa eventos de integração em registros, via **catálogo opt-in** de eventos auditáveis.
- Imutabilidade: sem caminho de UPDATE/DELETE na aplicação + trigger de bloqueio no PostgreSQL + revogação de privilégios do role da aplicação.
- Sanitizador defensivo anti-PII (denylist de chaves) aplicado ao payload antes de persistir.
- Contrato de consulta básica paginada (`IConsultaAuditoria`), tenant-scoped.
- Registro no catálogo dos eventos existentes desde a Fase 0/1 (ex.: `ContaCriada`, `ApiKeyRotacionada`, `ApiKeyRevogada`, `WebhookConfigAlterada`, `PlanoAlterado`, `ContaSuspensa` — nomes finais conforme B-tarefas das Fases 0/1).

**Fora:**
- Auditoria de acesso a certificado/CSC (`AssinarXml`, `GerarQrCode`, upload/destruição de A1) — Fase 2 apenas **registra novos eventos no catálogo**, sem mudar esta fundação.
- Endpoint HTTP de consulta (Portal/Backoffice, Fase 6); exportação; alertas sobre trilha; auditoria de leitura (queries GET); WORM/Object Lock de banco; particionamento físico (ver Riscos).

## Abordagem / Passos

TDD estrito (constraint global): cada passo abre com teste vermelho.

1. **Contratos** (projeto `BuildingBlocks.Auditoria.Contracts`):

```csharp
public sealed record RegistroAuditoria(
    Guid Id,
    Guid? ContaId,              // null apenas para ações de sistema/backoffice sem tenant (escopo de sistema — ver passo 6)
    string TipoEvento,          // nome versionado do evento de integração (ex.: "ApiKeyRevogada.v1")
    string Acao,                // verbo do catálogo (ex.: "apikey.revogada")
    string RecursoTipo,         // "ApiKey" | "Conta" | "WebhookConfig" | ...
    string RecursoId,           // id opaco do recurso (GUID em string)
    string Ator,                // quem: "apikey:nfh_live_ab12…" (só prefixo), "portal:usuarioId", "sistema:worker"
    DateTimeOffset OcorridoEm,  // timestamp do evento de origem
    DateTimeOffset RegistradoEm,// timestamp da materialização (UTC, gerado no insert)
    string CorrelationId,       // propagado request → outbox → inbox (Fase 0 observabilidade)
    Guid MessageId,             // id da mensagem de integração — chave de dedupe
    string? DadosJson);         // jsonb: SÓ identificadores/valores não sensíveis, pós-sanitização

public interface IConsultaAuditoria
{
    Task<PaginaDe<RegistroAuditoria>> ConsultarAsync(FiltroAuditoria filtro, CancellationToken ct);
}
public sealed record FiltroAuditoria(Guid ContaId, DateTimeOffset? De, DateTimeOffset? Ate,
    string? Acao, string? RecursoTipo, int Pagina = 1, int TamanhoPagina = 50); // ContaId obrigatório

public interface IAuditoriaEventCatalog   // opt-in: evento fora do catálogo é ignorado (sem erro)
{
    bool TryMap(EventoIntegracao evento, out RegistroAuditoriaNovo registro);
}
```

2. **Testes de unidade primeiro:** mapeamento catálogo (evento conhecido → registro com todos os campos; desconhecido → ignorado); sanitizador rejeita/remove chaves da denylist (`cpf`, `cnpjConsumidor`, `nome`, `senha`, `xml`, `pfx`, `csc`, `token`, `secret` — case-insensitive, com log de alerta `WARN` sem ecoar o valor); `FiltroAuditoria` sem `ContaId` não compila/valida.
3. **Migração** (schema `auditoria`): tabela com PK `id`, índices `(conta_id, registrado_em DESC)` e único em `message_id`; coluna `dados` jsonb; em seguida:

```sql
CREATE FUNCTION auditoria.bloquear_mutacao() RETURNS trigger AS
$$ BEGIN RAISE EXCEPTION 'registro_auditoria e append-only'; END $$ LANGUAGE plpgsql;
CREATE TRIGGER trg_append_only BEFORE UPDATE OR DELETE ON auditoria.registro_auditoria
  FOR EACH ROW EXECUTE FUNCTION auditoria.bloquear_mutacao();
REVOKE UPDATE, DELETE, TRUNCATE ON auditoria.registro_auditoria FROM <app_role>; -- ver Assunção A2
```

4. **Handler inbox** (biblioteca B3): dedupe primário pelo inbox (`messageId`); segunda barreira no insert (`ON CONFLICT (message_id) DO NOTHING`) — duplicata e reentrega fora de ordem jamais geram segundo registro nem falham o handler. Insert e marcação do inbox na mesma transação.
5. **Camada de aplicação sem mutação:** entidade EF somente-leitura pós-insert (sem setters públicos); nenhum método de update/delete no repositório; regra NetArchTest: nenhum módulo de negócio referencia a implementação (só `Contracts`).
6. **Consulta e escopo de tenant:** implementação de `IConsultaAuditoria` com filtro obrigatório por `conta_id`; a tabela participa do filtro global de tenant do DbContext base da Fase 0. Registros de **escopo de sistema** (`conta_id NULL`, ações de sistema/backoffice sem tenant) são gravados pelo handler sob o **bypass documentado** do filtro global (mecanismo padrão do kernel da Fase 0, mesmo usado pelo Worker fora de `BeginTenantScope`) e **não** são retornáveis por `IConsultaAuditoria` (que exige `ContaId`): a leitura desses registros fica explicitamente fora do MVP, reservada ao endpoint de Backoffice da Fase 6, que usará o mesmo bypass documentado. O teste de isolamento (CA6) cobre também que consulta por tenant jamais retorna registro com `conta_id NULL`.
7. **Retenção:** sem purga automática no MVP; documento `docs/decisions.md` §0 ganha entrada "retenção da trilha de auditoria ≥ 5 anos (espelha guarda fiscal); job de expurgo e particionamento adiados" — registrar na saída desta tarefa.

## Critérios de aceite (verificáveis)

1. `dotnet test --filter "FullyQualifiedName~Auditoria"` verde, com testes escritos antes da implementação (histórico de commits demonstra red→green).
2. Publicar evento catalogado via outbox de um módulo → exatamente 1 linha em `auditoria.registro_auditoria` com `conta_id`, `ator`, `acao`, `correlation_id` e `message_id` preenchidos (integração/Testcontainers).
3. `UPDATE` e `DELETE` diretos via SQL na tabela lançam `PostgresException` (trigger), mesmo por superusuário de aplicação; API pública dos contratos não expõe mutação (verificado por NetArchTest/compilação).
4. Reentrega da mesma mensagem (mesmo `messageId`, 2x, inclusive fora de ordem com outra mensagem no meio) → 1 registro, handler não lança.
5. Evento cujo payload contenha chave da denylist (`cpf`, `xml`, …) → registro persiste **sem** a chave, log `WARN` emitido sem o valor; nenhum registro jamais contém XML ou PII (asserção sobre `dados` em todos os testes de integração).
6. `ConsultarAsync` com conta A não retorna registros da conta B nem registros de escopo de sistema (`conta_id NULL`) (teste de isolamento de tenant); paginação e filtro por período/ação funcionam.
7. Evento não catalogado é consumido sem erro e sem registro (não envenena o inbox).

## Plano de testes / Evidências

| Tipo | Casos | Ferramenta |
|---|---|---|
| Unidade | catálogo (map/ignore), sanitizador denylist, montagem de `Ator` por origem (apikey-prefixo/portal/sistema), validação de filtro | xUnit, sem I/O |
| Integração | evento→registro (CA2); duplicata/fora-de-ordem (CA4); UPDATE/DELETE bloqueados (CA3); isolamento de tenant (CA6); evento não catalogado (CA7); PII barrada ponta a ponta (CA5) | Testcontainers/PostgreSQL |
| Arquitetura | módulos só referenciam `Contracts`; entidade sem setter público; tabela tenant-scoped com `conta_id` (anulável — escrita de escopo de sistema só via bypass documentado, passo 6) | NetArchTest (suite Fase 0) |

Evidências: saída do `dotnet test` no CI + migração revisada (trigger/REVOKE presentes no SQL gerado, `dotnet ef migrations script`).

## Dependências

- **B3 (outbox/inbox por módulo)** — bloqueante: o handler é um consumidor inbox; dedupe primário e transação vêm de lá.
- Fase 0 kernel: `ITenantContext`/filtro global, `BeginTenantScope` no Worker, `CorrelationId` propagado via outbox, suite NetArchTest, CI com Testcontainers.
- Eventos concretos das tarefas de Contas & Planos (Fase 1) para popular o catálogo — a fundação testa com eventos sintéticos se ainda não existirem (mesmo padrão do metering na Fase 1).

## Riscos e pontos de atenção

- **Assunção A1 (pende da matriz do Gate 0/A2):** solution reestruturada conforme Fase 0 (`BuildingBlocks/`), e schema `auditoria` com dono = kernel — exceção consciente à regra "schema pertence a módulo de negócio" (D-2026-07-01-10), a ratificar com o dono; alternativa é vivê-la dentro do schema de cada módulo consumidor (rejeitada aqui: fragmenta a trilha).
- **Assunção A2:** existirá role de banco dedicado da aplicação para o `REVOKE` ser efetivo (decisão de infra da Fase 0/AWS); até lá o trigger é a garantia real — por isso o CA3 testa o trigger, não o privilégio.
- **Assunção A3:** volume MVP (NFC-e SE, tenant piloto) dispensa particionamento; reavaliar quando eventos de emissão de alto volume entrarem no catálogo — se o custo de escrita aparecer, particionar por mês **antes** de reduzir o catálogo.
- **Assunção A4 (cobertura parcial do §2.2 — decisão explícita, a ratificar no Gate 0):** o design pede "quem/quando/de onde/o quê/**antes-depois**"; esta fundação cobre quem (`Ator`), quando (`OcorridoEm`/`RegistradoEm`) e o quê (`Acao`/`RecursoTipo`/`RecursoId`/`DadosJson`), mas **omite deliberadamente** (a) "antes-depois": eventos de integração não carregam estado anterior e diffs tenderiam a violar §2.3.6 (sem PII/XML nos eventos); e (b) "de onde" explícito (origem/IP): `Ator` identifica o canal (apikey/portal/sistema), não a origem de rede. Se a ratificação exigir cobertura plena, evolução prevista: campos opcionais `origem` e `dados_antes` jsonb sanitizado, aditivos, sem quebrar este contrato.
- Trigger não protege contra DBA malicioso (drop do trigger); mitigação futura fora do MVP (log shipping/WORM). Registrar como limitação conhecida, não resolver aqui.
- Denylist é defesa em profundidade, não a garantia primária — a regra de fronteira §2.3.6 (eventos sem PII/XML) continua valendo nos produtores; sanitização silenciosa demais pode mascarar violação do produtor, por isso o `WARN` do CA5 é obrigatório.
- Ordem/atraso do inbox: `RegistradoEm` ≠ `OcorridoEm` por design; consultas e futuras telas devem ordenar por `OcorridoEm` para leitura humana e por `RegistradoEm` para reconciliação técnica.
