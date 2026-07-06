# NotaFiscalHub.BuildingBlocks.Observability

Fundação de observabilidade da Fase 0 (Tarefa 7, spec `docs/superpowers/specs/tarefas/B7-observabilidade-fundacional.md`),
consumida pelos 2 hosts (`Api`, `Worker`) via um único ponto de bootstrap:

```csharp
builder.AddNfhObservability("nfh-api"); // ou "nfh-worker"
// ...
app.UseNfhObservability();
```

`AddNfhObservability` registra:

- **Logging estruturado (Serilog)**: console em JSON compacto, enriquecido por `PiiMaskingEnricher`
  (Camada 1 da política anti-PII, ver abaixo) e por `CorrelationId` (via `LogContext`, populado pelo
  `CorrelationIdMiddleware`/`OutboxDispatcher`).
- **`ICorrelationContext`**: valor ambiente (`AsyncLocal`) do CorrelationId do fluxo corrente — nunca
  vazio depois da borda (middleware HTTP na API; dispatcher da Outbox no Worker).
- **OpenTelemetry (traces)**: instrumentação ASP.NET Core (filtrando `/alive`/`/health`, ruído de
  probe), HttpClient, EF Core + Npgsql nativo (`Npgsql.OpenTelemetry`). Exporter OTLP condicional à
  configuração `Observability:Otlp:Endpoint` — vazio/ausente = sem exporter.
- **Health checks**: `AddHealthChecks().AddNpgSql(...)` condicional a
  `Observability:Postgres:ConnectionString` (tag `"ready"`).

`UseNfhObservability` registra o `CorrelationIdMiddleware` e mapeia `/alive` (liveness — nunca avalia
dependências, `Predicate = _ => false`) e `/health` (readiness — só checks com tag `"ready"`).

## Política anti-PII (3 camadas)

A política é a mesma tanto em logs quanto em traces — nenhuma das 3 camadas sozinha é suficiente, por
isso as 3 coexistem (mascaramento por nome é frágil por construção):

1. **Pipeline (`PiiMaskingEnricher` + `PiiFields`)**: mascara, por NOME de propriedade estruturada
   (case-insensitive), qualquer valor logado via `{Propriedade}` no template. `PiiFields.Denylist` é a
   lista central — `cpf`, `nome`, `email`, `senha`, `pfx`, `csc`, prefixo `xml*`, etc. viram `"***"`.
   `chaveAcesso` (chave de acesso NFe/NFCe) é a ÚNICA exceção: mascaramento PARCIAL
   (`{6 primeiros}...{4 últimos}`, ex. `350906...0097`) — ver `PiiFields.MascaradosParcialmente`.

   **Para adicionar um campo novo à denylist**: adicione o nome em `PiiFields.Denylist` (ou
   `PrefixosSensiveis`/`MascaradosParcialmente` conforme o caso) — um lugar só. Adicione também um caso
   ao teste `PiiMaskingEnricherTests` (T1).

2. **Compile-time (`CA2254` como erro)**: `Directory.Build.props` trata `CA2254` como erro —
   `_logger.LogInformation($"...")` (interpolação de string no template) não compila. Isso fecha a rota
   que escaparia da Camada 1: a Camada 1 só mascara PROPRIEDADES estruturadas, nunca o texto livre da
   mensagem — interpolar uma variável direto no template despeja o valor no texto, sem chance de
   mascaramento.

3. **Guardrail de teste (`PiiLogAssertions.AssertNoPii`)**: helper reutilizável por qualquer módulo a
   partir da Fase 1 — renderiza cada evento de log (mensagem + propriedades + exceção anexada) e falha
   se casar um padrão de CPF (`\d{3}\.?\d{3}\.?\d{3}-?\d{2}`) ou fragmento de XML fiscal (`<infNFe`,
   `<NFe`, `<dest>`). É a rede de segurança para o que escapar das Camadas 1/2.

**Convenção**: nunca logar um objeto de domínio/DTO inteiro (`{@Objeto}`); logs de negócio usam só
identificadores (`notaId`, `contaId`, `empresaId`, `status`, `cStat`).

## CorrelationId — API → Outbox → Worker

`CorrelationIdMiddleware` (API, antes da resolução de tenant): lê `X-Correlation-Id` do request
(aceita só `[A-Za-z0-9\-]{8,64}`; ausente/inválido → gera GUID novo — nunca confia em valor arbitrário
do cliente), popula `ICorrelationContext` + `LogContext` + tag no `Activity.Current`, devolve o mesmo
valor no header de resposta.

`OutboxPublisher<TDbContext>` lê `ICorrelationContext.CorrelationId` (injetado via construtor) e grava
no envelope (`OutboxMessage.CorrelationId`) — esse é o ponto que fecha a dívida técnica deixada pela
Tarefa 3 (`OutboxPublisher.CorrelationIdAtual()` antes usava `Activity.Current?.RootId` como
placeholder provisório).

`OutboxDispatcher<TDbContext>` (Worker), antes de invocar cada handler, abre um escopo de correlação
(`ICorrelationContext.Definir` + `LogContext.PushProperty` + tag no Activity) a partir do
`CorrelationId` da MENSAGEM — nunca herdado do ciclo de dispatch anterior (essencial para
redelivery/reordenação: cada mensagem processada tem seu próprio escopo).

`CorrelationId` é deliberadamente distinto do `traceId` W3C do OpenTelemetry — o RFC 7807 continua
expondo `traceId`; `CorrelationId` é o identificador estável usado por integradores externos
(ex.: contribuinte perguntando "o que aconteceu com a nota X" cita o CorrelationId do header de
resposta, não o traceId interno).

## O que é Fase 7 (fora de escopo aqui)

- Métricas de negócio (taxa autorização×rejeição, latência SEFAZ, contingência a vencer 24h).
- Dashboards e alertas.
- Check de S3/KMS em `/health` (aguarda provisionamento AWS da trilha paralela).
- SEFAZ como estado *degraded* em `/health` (só existe cliente SEFAZ a partir da Fase 3).
- Propagação do CorrelationId até a entrega de webhook (o envelope já carrega o valor; o consumo
  fica para a Fase 4).

A fundação entrega só os PONTOS DE EXTENSÃO (ex.: um `Meter` nomeado pode ser criado sem instrumentos
ainda) — instrumentar métricas de negócio antes da Fase 7 é ruptura de escopo.
