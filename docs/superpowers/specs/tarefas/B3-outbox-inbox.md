# Spec B3 — Biblioteca Outbox/Inbox por módulo
> Card: https://app.clickup.com/t/86e24c1gv | Grupo B | Gerada em 2026-07-02 por time de agentes (escritor especialista + revisor adversarial)

## Objetivo
Entregar em `BuildingBlocks/` a biblioteca Outbox/Inbox **instanciada por módulo** (nunca serviço central): publicação de eventos de integração na mesma transação do agregado, dispatcher in-process por módulo produtor, inbox por módulo consumidor com dedupe por `messageId`, retry com backoff, dead-letter após N tentativas, e — lição do legado (MSG0005) — **nenhum evento desaparece em silêncio**: evento sem handler registrado é marcado e alertado explicitamente.

## Contexto e referências
- Design §2.2 (kernel), §2.3.2 (outbox por módulo; inbox por consumidor), §2.3.3/§2.3.6 (eventos aditivos-só, só identificadores), §4.1 (par `Outbox`/`Inbox` em todo schema produtor/consumidor), §4.3 (CorrelationId ponta a ponta): `docs/superpowers/specs/2026-07-01-nota-fiscal-hub-produto-design.md`.
- Plano-mestre, Global Constraints + Fase 0 (item "Biblioteca Outbox/Inbox"): `docs/superpowers/plans/2026-07-01-nota-fiscal-hub-mvp-plano-mestre.md`.
- Decisões: D-2026-07-01-10 (webhooks executam no Worker via inbox de Contas & Planos): `docs/decisions.md` §0.
- Lição do legado: handoff `docs/superpowers/handoffs/2026-07-01-revisao-docs-e-gate0-handoff.md` §4 — MSG0005: 5 eventos sem handler engolidos pelo `OutboxRelayJob` central (`src/VisuFiscalHub.Infrastructure/Jobs/OutboxRelayJob.cs`). Este é o antipadrão que a B3 elimina.
- Integrações com outras tarefas do grupo: B7 (CorrelationId/observabilidade), tenant context (`BeginTenantScope`), NetArchTest.

**Assunções dependentes da matriz do Gate 0 (A2)** — validar antes de iniciar:
1. Parecer REESCREVER para o outbox legado (central, Hangfire): esta spec assume biblioteca nova sem dependência de Hangfire (`BackgroundService` + polling); se A2 mantiver Hangfire, só o *scheduler adapter* muda — contratos e tabelas desta spec permanecem.
2. PostgreSQL + EF Core 10 confirmados (uso de `FOR UPDATE SKIP LOCKED` e schema por módulo).
3. Dispatcher hospedado **somente no deployable Worker** (API apenas grava outbox); se A2 exigir dispatch também na API, a concorrência já está coberta pelo lock por linha.

## Escopo (dentro / fora)
**Dentro:** biblioteca em `BuildingBlocks/NotaFiscalHub.BuildingBlocks.Messaging/`; entidades/configurations EF `OutboxMessage`/`InboxMessage` (aplicadas no schema do módulo que instancia); API de publicação transacional; dispatcher por módulo; inbox com dedupe; política de retry/poison; política de evento órfão; job de retenção/limpeza; propagação de `CorrelationId` e `conta_id` (incl. whitelist `AddEventoDePlataforma` para eventos sem tenant); serialização/versionamento do envelope; test-kit (helpers para duplicata/reordenação); regra NetArchTest de payload de evento (com supressões).
**Fora:** transporte externo (RabbitMQ/SQS — o design exige que a troca não altere módulos); UI de redisparo de poison (Portal, fase futura — MVP redisparo via SQL/runbook); handlers de negócio concretos (fases 1–4); entrega de webhooks (Fase 4, consome esta biblioteca); auditoria append-only (tarefa própria, consome esta biblioteca).

## Abordagem / Passos
TDD estrito (constraint global): cada passo abaixo começa pelo teste que falha.

1. **Contratos** (projeto `Messaging.Abstractions`, sem dependência de EF):
```csharp
public abstract record EventoIntegracao   // payload: SÓ identificadores/status — nunca XML, PDF ou PII
{
    public Guid MessageId { get; init; } = Guid.CreateVersion7();
    public Guid? ContaId { get; init; }                    // null SÓ para tipos registrados via AddEventoDePlataforma<T>() (ver passo 4)
    public DateTimeOffset OcorridoEm { get; init; } = DateTimeOffset.UtcNow;
}
public interface IOutboxPublisher          // registrado por módulo, amarrado ao DbContext do módulo
{
    void Publicar<T>(T evento) where T : EventoIntegracao; // insere na Outbox DENTRO da transação corrente do agregado
}
public interface IInboxHandler<in T> where T : EventoIntegracao
{
    Task HandleAsync(T evento, MensagemContexto ctx, CancellationToken ct); // idempotente e tolerante a reordenação
}
public sealed record MensagemContexto(Guid MessageId, string CorrelationId, int Tentativa);
```
2. **Tabelas** (par por schema, criadas pela migration do módulo via `modelBuilder.AplicarOutboxInbox("emissao")`):
   - `Outbox`: `id` (PK = messageId), `tipo_evento` (nome lógico versionado, ex.: `empresas.empresa_criada.v1`), `payload` (jsonb), `conta_id`, `correlation_id`, `ocorrido_em`, `status` (`Pendente|Processada|SemHandler|Poison`), `tentativas`, `proxima_tentativa_em`, `processada_em`, `erro_ultimo`. Índice parcial em (`status`,`proxima_tentativa_em`) para `Pendente`.
   - `Inbox`: `message_id` + `handler` (PK composta — dedupe **por handler**, permitindo N handlers para o mesmo evento), `tipo_evento`, `processada_em`. Unique constraint é o mecanismo de dedupe; nunca "check-then-insert".
3. **Publicação transacional:** `IOutboxPublisher` usa o mesmo `DbContext`/transação do agregado (mesma `SaveChanges`); captura `CorrelationId` do `Activity.Current`/accessor do B7 e `ContaId` do `ITenantContext`. Sem transação ativa → exceção (nunca publicar fora de transação).
4. **Dispatcher por módulo** (`OutboxDispatcher<TDbContext>`, hosted service registrado por `AddOutboxInbox<TDbContext>(schema, opções)`): loop de polling (default 2s, configurável) → `SELECT ... FOR UPDATE SKIP LOCKED LIMIT lote` sobre `Pendente` vencidas → para cada mensagem: desserializa pelo registry de tipos; resolve handlers inscritos (`AddInboxHandler<TEvento, THandler>()`); por handler: abre scope DI + escopo de tenant conforme a regra abaixo + escopo de log com `CorrelationId` e `MessageId`; tenta `INSERT` na Inbox do consumidor — violação de unique = duplicata, **skip silencioso com log Debug** (é o caminho normal do at-least-once); executa o handler e commita inbox-insert + efeitos do handler na mesma transação (dedupe e efeito são atômicos).
   - **Regra de escopo de tenant (design §2.2 — "job sem escopo de tenant é erro, não processa tudo"):** `ContaId` preenchido → `BeginTenantScope(contaId)` obrigatório antes do handler. `ContaId = null` só é aceito para tipos declarados na whitelist explícita `AddEventoDePlataforma<TEvento>()` (eventos de plataforma sem tenant, ex.: manutenção/infra agendada; comentário justificando é obrigatório no registro) — nesses casos o handler roda **sem** escopo de tenant e qualquer acesso a dado tenant-scoped deve falhar pelo guard do kernel. `ContaId = null` em tipo NÃO registrado como plataforma → `Poison` imediato com `erro_ultimo = "evento tenant-scoped sem conta_id"` (nunca executar handler "para todos os tenants" nem sem escopo por omissão). `ContaId` preenchido em evento de plataforma → também `Poison` (registro inconsistente).
5. **Evento órfão (MSG0005 — comportamento explícito):** zero handlers resolvidos → status `SemHandler` (NÃO `Processada`), log **Warning** estruturado (`tipo_evento`, `messageId`) e incremento da métrica `outbox_mensagens_sem_handler_total`. Tipo não registrado no registry (não desserializável) → `Poison` imediato com `erro_ultimo` explicativo. Adicional de build: teste de arquitetura falha se um `EventoIntegracao` declarado em `Contracts` de um módulo não tiver nem handler nem registro explícito em `EventosSemConsumidorConhecido` (lista de supressão consciente, com comentário).
6. **Retry/poison:** falha de handler → rollback da transação daquele handler (outros handlers do mesmo evento não são afetados), `tentativas++`, `proxima_tentativa_em = agora + backoff` exponencial com jitter (base 5s, fator 2, teto 1h). Após `MaxTentativas` (default 10, config por módulo) → `Poison` + métrica `outbox_mensagens_poison_total` (alerta em B7/Fase 7) + log Error com stack. Poison não bloqueia a fila (mensagens seguintes continuam — ordering não é garantido por contrato).
7. **Ordering:** contrato explícito **sem garantia de ordem** (paralelismo do lote + retry reordenam naturalmente). A biblioteca não implementa ordenação; o test-kit obriga cada handler a provar tolerância (item de testes 3).
8. **Retenção/limpeza:** hosted service por módulo apaga em lotes de 1000: `Outbox` `Processada` com mais de 7 dias; `Inbox` com mais de 30 dias (janela de dedupe — documentar: duplicata após 30 dias não é dedupada; handlers idempotentes por natureza cobrem o resíduo). `Poison` e `SemHandler` **nunca** são apagados automaticamente: um evento órfão precisa permanecer consultável e redisparável até resolução registrada (handler implementado + redisparo via runbook, ou supressão consciente em `EventosSemConsumidorConhecido`) — apagar `SemHandler` por idade recriaria exatamente o "evento sumindo em silêncio" do MSG0005 que esta spec elimina.
9. **Test-kit** (`Messaging.TestKit`): `OutboxTestHarness` para despachar em memória com duplicação e embaralhamento determinísticos — usado pelos módulos nas fases 1–4 para cumprir a constraint "testados com duplicata/fora-de-ordem desde o monólito".

## Critérios de aceite (verificáveis)
1. Rollback da transação do agregado ⇒ zero linhas na `Outbox` (evento e agregado são atômicos); commit ⇒ exatamente 1 linha.
2. Mesmo `messageId` despachado 2× ⇒ handler executa efeito **1×** (Inbox dedupa); com 2 handlers inscritos, cada um executa 1×.
3. Handler que lança exceção ⇒ mensagem volta a `Pendente` com `proxima_tentativa_em` futura e backoff crescente entre tentativas; na 10ª falha ⇒ `Poison`, métrica incrementada, demais mensagens da fila processadas normalmente.
4. Evento sem handler ⇒ linha termina `SemHandler` (consultável), log Warning emitido, métrica incrementada — **nunca** `Processada`; build falha (NetArchTest) para evento de contrato sem handler e sem supressão explícita.
5. Handler executa sob `BeginTenantScope(contaId)` e o `CorrelationId` da requisição de origem aparece nos logs do handler (asserção sobre escopo de log).
6. NetArchTest: tipos derivados de `EventoIntegracao` só expõem propriedades de tipos permitidos (Guid, enum, string curta de status/chave, DateTimeOffset, decimal, int, bool) — propriedade `byte[]`/stream ou nome casando `(?i)(xml|pdf|cpf|senha|certificado|payload)` falha o build, **com duas válvulas contra falso positivo**: (a) isenção automática para propriedade `Guid`/`Guid?` cujo nome termina em `Id` (identificadores como `CertificadoId` em `CertificadoProximoDoVencimento`/`CertificadoExpirado`, previstos na Fase 2, são exatamente o payload que o design §2.3.3 manda usar); (b) lista de supressão explícita `PropriedadesDeEventoPermitidas` (par tipo+propriedade, com comentário justificando — mesmo mecanismo da supressão de handlers do passo 5). Teste do próprio critério cobre os dois lados: `CertificadoId` (Guid) passa; `XmlAutorizado` (string) e `CertificadoPfx` (byte[]) falham.
7. Dois dispatchers concorrentes sobre a mesma tabela ⇒ nenhuma mensagem processada 2× (SKIP LOCKED) — teste de integração com 2 hosts.
8. Job de limpeza remove `Processada` > 7 dias e preserva `Pendente`, `Poison` **e `SemHandler`** — inclusive `Poison`/`SemHandler` com mais de 7 dias (asserção explícita: linha `SemHandler` antiga continua consultável e redisparável).
9. Escopo de tenant: mensagem com `ContaId = null` de tipo **não** registrado em `AddEventoDePlataforma` ⇒ `Poison` imediato, handler **não** executa, `erro_ultimo` preenchido; tipo registrado como plataforma ⇒ handler executa sem `BeginTenantScope` e asserção confirma `ITenantContext` vazio durante a execução.

## Plano de testes / Evidências
- **Unidade (xUnit, sem I/O):** cálculo de backoff (com jitter limitado por seed), transições de status, registry de tipos, política de órfão, serialização round-trip com campo desconhecido (tolerância aditiva — consumidor ignora campo extra).
- **Integração (Testcontainers/PostgreSQL):** critérios 1–3, 7, 8 e 9 contra banco real; duplicata via re-INSERT forçado na fila; fora-de-ordem via embaralhamento do lote; crash simulado (kill do host entre inbox-insert e commit ⇒ reentrega e dedupe); retenção com linhas `SemHandler`/`Poison` antigas plantadas (critério 8).
- **Arquitetura (NetArchTest):** critérios 4 e 6, incluindo os dois mecanismos de supressão (`EventosSemConsumidorConhecido` e `PropriedadesDeEventoPermitidas`) e a isenção `Guid *Id`; `Messaging.Abstractions` sem referência a EF/Npgsql.
- **Evidências no PR:** saída dos testes verdes + print da tabela `Outbox` de um cenário poison + trecho de log mostrando Warning de `SemHandler` com `CorrelationId`.

## Dependências
- **Precede:** todas as publicações/consumos das Fases 1–4 (metering, `EmpresaLocal`, `NotaConsulta`, webhooks, auditoria) — B3 é bloqueante do primeiro evento real.
- **Depende de:** decisão da matriz A2 (assunções acima); tenant context do kernel (`ITenantContext`, `BeginTenantScope`) e accessor de `CorrelationId` do B7 — se B7 não estiver pronto, usar `Activity.Current?.RootId` como interface provisória e trocar via DI (registrar como dívida no PR).

## Riscos e pontos de atenção
- **Handler "idempotente" só no papel:** o dedupe da Inbox cobre reentrega da MESMA mensagem; efeito duplicado por mensagens distintas de negócio (ex.: dois `NotaAutorizada` do mesmo agregado por bug do produtor) exige idempotência natural no handler (upsert por chave de negócio). O test-kit ajuda, o code review cobra.
- **Poison acumulando sem dono:** sem UI no MVP, poison depende de alerta (B7) + runbook; incluir consulta SQL pronta no runbook e revisar volume semanalmente até a Fase 7.
- **Polling × latência:** 2s de poll é suficiente para o MVP (webhook/metering são pós-commit); não otimizar com LISTEN/NOTIFY agora — registrar como evolução.
- **Transação longa no handler:** handler que chama I/O externo (S3, HTTP de webhook) segura a transação do inbox-insert; padrão obrigatório: handler persiste intenção local e o trabalho externo roda em job próprio do módulo (o entregador de webhook da Fase 4 já nasce assim).
- **Janela de dedupe de 30 dias:** redisparo manual de mensagem com mais de 30 dias reexecuta handlers — o runbook de redisparo deve avisar.
