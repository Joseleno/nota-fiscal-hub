# Spec B7 — Observabilidade fundacional no kernel
> Card: https://app.clickup.com/t/86e24c1k6 | Grupo B | Gerada em 2026-07-02 por time de agentes (escritor especialista + revisor adversarial)

## Objetivo
Entregar no kernel (`BuildingBlocks/`) a fundação de observabilidade da Fase 0 para os 2 deployables (API e Worker): logging estruturado Serilog **sem PII** com política enforçada por código e por teste; `CorrelationId` gerado/propagado na borda e **através do outbox** (integra com B3); OpenTelemetry básico (traces HTTP + EF Core, resource, exporter configurável); health checks `/alive` × `/health`. Métricas de negócio, alertas e dashboards ficam explicitamente para a Fase 7 (design §4.3).

## Contexto e referências
- Design: `docs/superpowers/specs/2026-07-01-nota-fiscal-hub-produto-design.md` — §4.3 (observabilidade: logs sem PII, CorrelationId request→outbox→worker→webhook, OTel, `/alive` vs `/health` com SEFAZ como *degraded*), §2.2 (kernel cross-cutting), §2.3.6 (eventos nunca carregam XML/PDF/PII), §3.1 (RFC 7807 com `traceId`), §5 (LGPD: CPF/nome de consumidor são dados de titular sob operação do hub).
- Plano-mestre: `docs/superpowers/plans/2026-07-01-nota-fiscal-hub-mvp-plano-mestre.md` — Fase 0, item "Observabilidade fundacional no kernel"; **Global Constraints**: "Erros da API em RFC 7807; logs estruturados sem PII", "Eventos de integração carregam identificadores, nunca XML/PDF/PII", "TDD em todas as fases".
- Decisões: `docs/decisions.md` §0 — D-2026-07-01-07 (um ambiente produtivo; `deployment.environment` distingue por configuração, não por stack); D-2026-07-01-10 (webhooks executam no Worker via inbox — o CorrelationId precisa sobreviver a esse salto).
- Handoff: `docs/superpowers/handoffs/2026-07-01-revisao-docs-e-gate0-handoff.md` — Gate 0 pendente de matriz final (A2); legado tem Serilog/observabilidade próprios cujo aproveitamento depende dessa matriz.

## Escopo (dentro / fora)
**Dentro (Fase 0):**
1. `BuildingBlocks/Observability`: bootstrap único `AddNfhObservability(builder, serviceName)` usado por API e Worker (Serilog + OTel + health base) — configuração idêntica, sem copiar/colar entre hosts.
2. Política anti-PII em 3 camadas (ver Abordagem): denylist de propriedades mascaradas no pipeline Serilog, proibição de interpolação em templates (analyzer), teste-guardrail com sink em memória.
3. `CorrelationId`: middleware na borda (aceita `X-Correlation-Id` válido ou gera GUID), disponível via `ICorrelationContext` (AsyncLocal), enriquecido em todo log, devolvido no header de resposta, gravado no envelope do outbox (B3) e restaurado pelo dispatcher/inbox no Worker antes de invocar o handler.
4. OTel traces: instrumentação ASP.NET Core + HttpClient + EF Core/Npgsql; resource (`service.name` = `nfh-api`/`nfh-worker`, `service.version`, `deployment.environment`); exporter OTLP com endpoint por configuração (`Observability:Otlp:Endpoint`; vazio = sem exporter; console opcional em dev). `CorrelationId` como tag do Activity raiz — correlaciona log↔trace sem substituir o `traceId` W3C (são identificadores distintos; o RFC 7807 continua expondo `traceId`).
5. Health checks nos 2 deployables: `/alive` (liveness, processo apenas) e `/health` (readiness: PostgreSQL via `AddNpgSql`). Worker expõe host HTTP mínimo só para esses 2 endpoints.

**Fora (delimitado):** métricas de negócio do §4.3 (taxa autorização×rejeição, latência SEFAZ, contingência a vencer 24h — alerta nº 1), dashboards e alertas → **Fase 7/I1**; check de S3/KMS em `/health` → quando o provisionamento AWS da trilha paralela da Fase 0 existir (registrar TODO nomeado); check SEFAZ como *degraded* → Fase 3 (só então existe cliente SEFAZ); propagação do CorrelationId até a **entrega de webhook** → Fase 4 (o envelope já o carrega; a Fase 4 só consome).

## Abordagem / Passos
TDD estrito (constraint global): cada passo abaixo começa pelo teste vermelho correspondente do Plano de testes.

1. **Contratos no kernel** (`BuildingBlocks/Observability`):
```csharp
public interface ICorrelationContext
{
    string CorrelationId { get; }   // nunca nulo após a borda; Worker: setado pelo inbox
}

public static class ObservabilityExtensions
{
    // Serilog (console JSON compacto) + política anti-PII + OTel + health base
    public static IHostApplicationBuilder AddNfhObservability(
        this IHostApplicationBuilder builder, string serviceName);
    public static WebApplication UseNfhObservability(this WebApplication app); // middleware + /alive + /health
}
```
2. **Política anti-PII (enforcement em 3 camadas):**
   - **Camada 1 — pipeline:** `PiiMaskingEnricher`/destructuring policy que mascara, por nome de propriedade (case-insensitive, denylist central `PiiFields`), qualquer propriedade estruturada: `cpf`, `cnpjConsumidor`, `documento`, `nome`, `nomeConsumidor`, `destinatario`, `email`, `telefone`, `endereco`, `xml*` (prefixo), `senha`, `pfx`, `csc`. `chaveAcesso` é mascarada parcialmente (`{6 primeiros}...{4 últimos}` — design §4.3: "CPF/chave mascarados"). Valor mascarado = `"***"`; nunca truncamento reversível para CPF/nome/XML.
   - **Camada 2 — compile-time:** templates de mensagem constantes obrigatórios — `CA2254` e regras Serilog do analyzer (`Serilog.Analyzer`) como **erro** no `.editorconfig`/`Directory.Build.props`; interpolação de string em chamada de log falha o build (fecha a rota que escapa da Camada 1).
   - **Camada 3 — guardrail de teste:** helper de teste no kernel (`PiiLogAssertions.AssertNoPii(logEvents)`) que renderiza cada evento e falha se casar regex de CPF (`\d{3}\.?\d{3}\.?\d{3}-?\d{2}`), fragmento de XML fiscal (`<infNFe`, `<NFe`, `<dest>`) ou valores-sentinela do cenário. Reutilizável pelos módulos nas fases 1–4.
   - **Convenção documentada no código:** nunca logar objeto de domínio/DTO inteiro (`{@Objeto}` só para tipos do kernel marcados seguros); logs de negócio usam apenas identificadores (`notaId`, `contaId`, `empresaId`, `status`, `cStat`).
3. **CorrelationId:** middleware na API (antes de auth): lê `X-Correlation-Id` (aceita só `[A-Za-z0-9\-]{8,64}`; inválido → gera novo), popula `ICorrelationContext`, `LogContext.PushProperty("CorrelationId", ...)`, tag no `Activity.Current`, header na resposta. **Integração com B3:** o envelope do outbox ganha entrada de header `correlation-id` preenchida pelo publisher a partir de `ICorrelationContext`; o dispatcher/inbox do Worker, ao consumir, abre escopo (`ICorrelationContext` + `LogContext` + tag no Activity do handler) **antes** de invocar o handler — jobs sem mensagem de origem geram CorrelationId próprio (nunca vazio).
4. **OTel:** `builder.Services.AddOpenTelemetry().WithTracing(...)` com `AddAspNetCoreInstrumentation` (filtrando `/alive` e `/health` — ruído), `AddHttpClientInstrumentation`, `AddEntityFrameworkCoreInstrumentation`/Npgsql com `SetDbStatementForText = true` e **parâmetros SQL desabilitados** (parâmetro pode conter CPF/XML — mesma política anti-PII vale para traces). Exporter OTLP condicional à configuração.
5. **Health:** `AddHealthChecks().AddNpgSql(...)`; API mapeia `/alive` (`Predicate = _ => false`) e `/health`; Worker sobe `WebApplication` mínimo na porta de management com os mesmos 2 endpoints.
6. **Wire-up nos hosts** API e Worker + documentação curta em `BuildingBlocks/Observability/README.md` (denylist, como estender, o que é Fase 7).

## Critérios de aceite (verificáveis)
1. `dotnet test` verde com os testes do plano abaixo; suíte NetArchTest existente continua verde (kernel não referencia módulos de negócio).
2. Chamada de log com string interpolada não compila (CA2254/analyzer como erro) — verificável com snippet de compilação negativa ou revisão do `Directory.Build.props`.
3. Requisição à API com dados de consumidor (CPF/nome no payload) não produz **nenhum** evento de log contendo CPF, nome ou fragmento de XML — verificado pelo guardrail (teste T3).
4. `curl -H "X-Correlation-Id: abc-123"` → resposta contém `X-Correlation-Id: abc-123`; sem header, resposta contém um GUID; todo log da requisição carrega a propriedade `CorrelationId` com esse valor.
5. Mensagem publicada no outbox durante a requisição carrega `correlation-id` no envelope; o log emitido pelo handler no Worker exibe o **mesmo** `CorrelationId` (teste T4).
6. Com `Observability:Otlp:Endpoint` vazio a aplicação sobe sem exporter (sem erro/retry em log); com endpoint definido, spans de HTTP e EF são exportados (verificado com exporter em memória no teste T5); nenhum span de EF contém valor de parâmetro SQL.
7. `GET /alive` → 200 sempre que o processo responde; `GET /health` → 200 com Postgres no ar e 503 com Postgres derrubado — nos **dois** deployables.

## Plano de testes / Evidências
| # | Teste (escrito ANTES da implementação) | Tipo |
|---|---|---|
| T1 | `PiiMaskingEnricher`: propriedade da denylist → `"***"`; `chaveAcesso` → máscara parcial; propriedade fora da denylist intacta | Unidade |
| T2 | CorrelationId middleware: header válido preservado; ausente/inválido → GUID; presente em `ICorrelationContext` e na resposta | Unidade (TestServer) |
| T3 | **Guardrail PII**: cenário de emissão fake com CPF/nome de consumidor no payload → sink em memória → `AssertNoPii` (regex CPF, nome-sentinela, `<infNFe`) sobre TODOS os eventos renderizados, incl. log de exceção com o payload no contexto | Integração (TestServer + sink) |
| T4 | **CorrelationId atravessa API→outbox→handler**: request com `X-Correlation-Id` → publica via outbox B3 → dispatcher processa → log do handler e `ICorrelationContext` no handler têm o mesmo valor; mensagem duplicada (redelivery) preserva o valor original | Integração (Testcontainers Postgres + dispatcher B3) |
| T5 | OTel: exporter em memória captura span HTTP e span EF; span EF sem valores de parâmetro; resource com `service.name` esperado; `/health` não gera span | Integração |
| T6 | Health: `/alive` 200; `/health` 200 → parar container Postgres → 503 (API e Worker) | Integração (Testcontainers) |

Evidências no PR: saída do `dotnet test`, trecho de log JSON real do T4 mostrando o mesmo `CorrelationId` nos dois processos lógicos, e diff do `Directory.Build.props` com os analyzers como erro.

## Dependências
- **B3 (biblioteca outbox/inbox)** — o envelope precisa de headers/metadata; se B3 ainda não expõe, esta tarefa contribui a extensão (campo `Headers` no envelope + hook de escopo no dispatcher) via PR coordenado. T4 bloqueado até o dispatcher existir.
- **Estrutura da solution da Fase 0** (`Api/`, `Worker/`, `BuildingBlocks/`) criada — pré-requisito do wire-up.
- **A2 (matriz do Gate 0)** — ver assunções abaixo.
- Provisionamento AWS (trilha paralela) — apenas para o TODO de checks S3/KMS; não bloqueia.

## Riscos e pontos de atenção
- **Assunção dependente de A2 (matriz Gate 0):** esta spec assume solution **nova** com kernel próprio (matriz preliminar do handoff §3: "reestruturar solution nova"). Se a matriz final mandar aproveitar o Serilog/middleware do legado, a Camada 1 e o middleware são portados, mas contratos, denylist e testes T1–T6 desta spec permanecem o critério de aceite. Nomes de projetos/namespaces (`BuildingBlocks.Observability`, `nfh-api`/`nfh-worker`) são provisórios até A2.
- **Mascaramento por nome é frágil por construção** — por isso as 3 camadas: pipeline cobre propriedade estruturada, analyzer fecha interpolação, guardrail pega o que escapar. A denylist é central e única; adicionar campo novo de PII exige tocar 1 lugar + 1 caso no T1.
- **PII em traces, não só em logs:** parâmetros SQL e request bodies nunca entram em spans (passo 4). Revisar também `Activity.SetTag` manual futuro — regra documentada no README do kernel.
- **CorrelationId ≠ traceId W3C:** manter os dois deliberadamente (integrador externo usa CorrelationId; RFC 7807 expõe traceId). Não tentar unificar agora — reavaliar na Fase 7 com o backend de traces escolhido.
- **Escopo-fluência:** a tentação de já criar métricas (contador de emissão etc.) é ruptura de escopo — a fundação entrega *pontos de extensão* (Meter nomeado pode ser criado, sem instrumentos); instrumentos de negócio só na Fase 7/I1.
- Redelivery/fora-de-ordem do outbox (constraint global) não pode corromper o escopo de correlação: o escopo é aberto por mensagem, nunca herdado de processamento anterior (coberto em T4).
