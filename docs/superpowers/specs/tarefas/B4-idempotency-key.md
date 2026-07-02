# Spec B4 — Middleware de Idempotency-Key (kernel)
> Card: https://app.clickup.com/t/86e24c1hy | Grupo B | Gerada em 2026-07-02 por time de agentes (escritor especialista + revisor adversarial)

## Objetivo
Implementar no kernel (`BuildingBlocks/`) o middleware de `Idempotency-Key` da API pública: reenvio da mesma requisição devolve a **resposta original** (status + corpo + headers relevantes); mesma key com payload diferente → `409` RFC 7807; requisições concorrentes com a mesma key → primeiro vence. Obrigatório em `POST /v1/nfce` (design §3.1; Global Constraint do plano-mestre).

## Contexto e referências
- Design §2.2 (kernel: "idempotência de recebimento na API"), §3.1 (semântica), §3.4 (linha 409), §3.7 (`tpAmb`); §6 risco "nota duplicada".
- Plano-mestre: Global Constraints ("`Idempotency-Key` obrigatória na emissão; mesma key + payload diferente → 409"; TDD; RFC 7807; logs sem PII) e Fase 0 (item "Middleware de Idempotency-Key").
- Decisões: D-2026-07-01-02/04 (dedupe profundo de transmissão é da Emissão via `ConsultarStatus` — o middleware NÃO substitui isso), D-2026-07-01-05 (202 devolve dados de impressão → precisa ser replayável), D-2026-07-01-09 (`tpAmb` por ambiente).
- Alinhado ao draft IETF `draft-ietf-httpapi-idempotency-key-header` (409 para conflito e para requisição original ainda em processamento).

## Escopo (dentro / fora)
**Dentro:** endpoint filter/middleware ASP.NET Core; store persistido (PostgreSQL, schema do kernel); metadados de rota (`RequireIdempotencyKey()` opt-in obrigatório / `AcceptIdempotencyKey()` opcional); problem types RFC 7807; job de expiração no Worker; testes unitários + integração (Testcontainers).
**Fora:** idempotência interna da Emissão (transação de numeração, reconciliação por `ConsultarStatus` — Fase 3); idempotência de handlers de eventos (inbox, dedupe por `messageId` — item próprio da Fase 0); rate limiting; Portal (usa JWT, não passa por este pipeline).

## Abordagem / Passos
TDD estrito (constraint global): cada passo abaixo começa pelos testes do item correspondente do plano de testes.

1. **Contratos no kernel** (`BuildingBlocks.Idempotency`):
```csharp
public sealed record IdempotencyRecord(
    Guid ContaId, string Ambiente,          // "producao" | "homologacao" — derivado da credencial (nfh_live_/nfh_test_)
    string Key, string Rota,                // template da rota, ex.: "POST /v1/nfce"
    string PayloadHashSha256,               // hash dos BYTES crus do corpo
    IdempotencyState Estado,                // EmProcessamento | Concluida
    int? RespostaStatus, string? RespostaCorpo, string? RespostaContentType, string? RespostaLocation,
    DateTimeOffset CriadaEm, DateTimeOffset ExpiraEm);

public enum IdempotencyState { EmProcessamento = 1, Concluida = 2 }

public interface IIdempotencyStore
{
    /// Tenta inserir registro EmProcessamento. Retorna Inserted (inclui takeover de órfão — ver passo 5),
    /// ou o registro existente vigente (corrida perdida/replay).
    Task<IdempotencyBeginResult> BeginAsync(IdempotencyRecord novo, CancellationToken ct);
    Task CompleteAsync(Guid contaId, string ambiente, string rota, string key, int status, string corpo,
                       string contentType, string? location, CancellationToken ct);
    /// Remove o registro EmProcessamento (falha 5xx/exceção) para permitir nova tentativa real.
    Task ReleaseAsync(Guid contaId, string ambiente, string rota, string key, CancellationToken ct);
}
```
2. **Tabela** `kernel.idempotency_registro` — PK `(conta_id, ambiente, rota, key)` (unicidade que resolve a corrida no banco; a **rota compõe a chave**: o escopo da key é o endpoint, alinhado ao draft IETF — a mesma key reutilizada em `POST /v1/nfce` e `POST /v1/inutilizacoes` gera registros independentes, sem 409 espúrio nem replay cruzado entre rotas); colunas conforme o record; índice em `expira_em`. `INSERT` de `BeginAsync` em transação própria, curtíssima, **committada antes** de o handler executar (nunca atravessa I/O da emissão — mesmo princípio da D-2026-07-01-04).
3. **Endpoint filter** (ordem no pipeline: após autenticação/tenant context, antes do handler):
   - Escopo de rotas: só métodos de escrita (POST/PUT/PATCH) sob `/v1` que declararem metadado. `POST /v1/nfce` → `RequireIdempotencyKey()`; demais escritas do §3.2 → `AcceptIdempotencyKey()` (processam sem key normalmente). GET/DELETE e rotas sem metadado: filter é no-op.
   - Validação da key: obrigatória onde required (ausente → `400` problem type `idempotency_key_obrigatoria`); 1–255 chars visíveis; inválida → `400 idempotency_key_invalida`.
   - Fluxo: lê o corpo bruto (buffering habilitado), computa SHA-256, chama `BeginAsync`:
     - **Inserted** → executa o handler capturando a resposta (body interceptado); status < 500 → `CompleteAsync` (armazenar inclusive 4xx e o **202 de contingência**); status ≥ 500 ou exceção → `ReleaseAsync` + resposta de erro normal (retry do cliente re-executa; a proteção contra dupla transmissão SEFAZ é da Emissão, não daqui).
     - **Existente + `Concluida` + hash igual** → replay: devolve status/corpo/`Content-Type`/`Location` originais + header `Idempotency-Replayed: true`. Nenhum handler executa. Replay do 202 devolve os mesmos dados de impressão mesmo que a nota já tenha sido regularizada (o integrador reconcilia por `GET /v1/nfce/{id}`/webhook — design §3.5/§3.6).
     - **Existente + hash diferente** (qualquer estado) → `409` problem type `idempotency_key_conflito` (não revela o payload original; inclui `traceId`).
     - **Existente + `EmProcessamento` + hash igual** (corrida concorrente) → primeiro vence; o perdedor recebe `409` problem type `requisicao_em_processamento` + `Retry-After: 2`. Sem espera bloqueando thread/conexão.
4. **TTL e retenção:** `ExpiraEm = CriadaEm + 24h` (configurável por rota; default único no MVP). Racional: cobre o ciclo de retry de PDV e a janela de contingência de 24h (D-2026-07-01-02); após expirar, a mesma key é tratada como requisição nova — a defesa contra duplicata tardia é da Emissão. Job de expiração no Worker roda a cada hora em **varredura por tenant**, respeitando a Global Constraint do plano-mestre (`BeginTenantScope` em todo job do Worker; design §2.2: job sem escopo de tenant é erro): enumera as `conta_id` distintas com registros vencidos e, para cada uma, abre `BeginTenantScope(contaId)` e faz delete físico em lote por `expira_em`. Esta spec **não cria exceção** ao constraint; se a varredura por tenant se mostrar cara em produção, propor exceção explícita registrada em `docs/decisions.md` (mesmo tratamento da exceção NetArchTest do design §2.5).
5. **Órfão `EmProcessamento` (crash antes de Complete/Release):** o TTL de 24h **não** é o mecanismo de liberação do órfão. Registro `EmProcessamento` com `CriadaEm` mais velho que `OrphanTimeout` (config, default 60s — ordem de grandeza acima do timeout síncrono do Motor, ~5s) é considerado abandonado e é reaproveitado pelo próprio `BeginAsync`: ao encontrar existente nesse estado vencido, executa **takeover atômico** no banco (`UPDATE ... SET criada_em = now(), payload_hash = @hash WHERE ... AND estado = 'EmProcessamento' AND criada_em < now() - @orphanTimeout`; 1 linha afetada = venceu) e retorna `Inserted` — o handler executa normalmente. Dois retries pós-crash concorrentes: só um vence o takeover; o perdedor enxerga o registro renovado e cai no caso corrida do passo 3 (`409 requisicao_em_processamento` + `Retry-After`). O job de expiração remove apenas registros com `ExpiraEm` vencido, em qualquer estado.
6. **`tpAmb` no escopo:** o ambiente compõe a PK e vem da **credencial** (`nfh_test_`/`nfh_live_`), não da empresa — disponível na borda antes de qualquer lookup, e consistente com §3.7 (key de teste só opera empresas em homologação). Mesma key em homologação e produção nunca colide.
7. **Observabilidade:** log estruturado sem PII (key é dado do integrador — logar apenas hash truncado da key), `CorrelationId`, métricas `idempotency_replays_total`, `idempotency_conflitos_total`, `idempotency_corridas_total`.

## Critérios de aceite (verificáveis)
1. `POST /v1/nfce` sem `Idempotency-Key` → `400` RFC 7807 `idempotency_key_obrigatoria`.
2. Dois `POST /v1/nfce` sequenciais, mesma key + mesmo corpo (bytes idênticos): handler executa **1 vez**; 2ª resposta = mesmo status, corpo, `Content-Type` e `Location` da 1ª + `Idempotency-Replayed: true`.
3. Mesma key + corpo diferente → `409` `idempotency_key_conflito` com `traceId`; handler não executa.
4. N requisições paralelas (N ≥ 8), mesma key + mesmo corpo: exatamente 1 executa o handler; as demais recebem replay ou `409 requisicao_em_processamento` com `Retry-After`; nunca 2 execuções (verificado por contador no handler fake).
5. Resposta `202` (contingência) é armazenada e replayada como `202` idêntico, sem re-disparar emissão.
6. Resposta `500`/exceção não é armazenada: retry com a mesma key executa o handler de novo.
7. Key idêntica com credencial `nfh_test_` e `nfh_live_` da mesma conta → dois registros independentes (sem replay cruzado); idem entre contas distintas.
8. Após `ExpiraEm`, mesma key + mesmo corpo executa o handler novamente (requisição nova).
9. Rota com `AcceptIdempotencyKey()` processa sem key; rota GET ignora o header mesmo se enviado.
10. Mesma key + mesma conta/ambiente em **rotas diferentes** (ex.: `POST /v1/nfce` e `POST /v1/inutilizacoes`): dois registros independentes; ambos os handlers executam, sem `409` nem replay cruzado — mesmo com corpos de bytes idênticos.
11. Órfão: registro `EmProcessamento` com `CriadaEm` mais velho que `OrphanTimeout` (relógio injetado) + novo request com a mesma key → takeover: `BeginAsync` retorna `Inserted` e o handler executa de novo; dois requests concorrentes pós-crash → exatamente 1 takeover vence, o outro recebe `409 requisicao_em_processamento`. Antes de vencido o `OrphanTimeout`, vale o critério 4 (corrida).
12. Job de expiração roda com `BeginTenantScope` por conta (verificável por spy/log do escopo); remove só registros com `ExpiraEm` vencido.
13. NetArchTest: `BuildingBlocks.Idempotency` não referencia nenhum módulo de negócio.

## Plano de testes / Evidências
- **Unidade (xUnit, sem I/O):** validação da key; cálculo do hash sobre bytes crus (corpos com whitespace diferente → hashes distintos → 409, comportamento documentado); máquina de decisão do filter (Inserted/replay/conflito/corrida/takeover de órfão — critério 11) com `IIdempotencyStore` fake; seleção de headers replayados.
- **Integração (Testcontainers + WebApplicationFactory):** critérios 1–12 contra PostgreSQL real; corrida do critério 4 e takeover concorrente do critério 11 com `Task.WhenAll` e barreira de sincronização no handler fake; expiração do critério 8 e órfão do critério 11 com relógio injetado (`TimeProvider`); job de limpeza (critério 12) remove expirados, não remove vigentes e abre escopo de tenant por conta.
- **Evidências:** saída verde da suíte no CI (build → unidade+arquitetura → integração, Fase 0); commits demonstrando testes-antes (RED→GREEN por passo).

## Dependências
- Fase 0: tenant context (`ITenantContext` resolvido na borda fornece `ContaId`) e pipeline de autenticação que expõe o ambiente da credencial — em testes, ambos podem ser fakes do host de teste; a integração real com API keys fecha na Fase 1.
- Schema/migrations do kernel (infra de banco da Fase 0).
- `TimeProvider` injetável (padrão do kernel) para testes de expiração.

## Riscos e pontos de atenção
- **Middleware não é a última defesa contra nota duplicada:** após TTL ou `ReleaseAsync`, a Emissão precisa do dedupe profundo (numeração transacional + `ConsultarStatus`, D-2026-07-01-02/04). Não vender esta tarefa como solução completa do risco §6.
- **Hash por bytes crus:** cliente que re-serializa o JSON com formatação diferente recebe 409. Documentar no guia do integrador ("reenvie a requisição idêntica"). Canonicalização de JSON foi descartada (custo/ambiguidade).
- **Corpo grande:** buffering do request e captura da resposta têm custo de memória; limitar tamanho de corpo armazenado (config, default 256 KB — respostas do §3.4 ficam muito abaixo) e rejeitar armazenamento acima disso com log de alerta.
- **`Retry-After` curto no perdedor da corrida:** PDVs devem re-tentar; se o vencedor cair em contingência (~5s de timeout do Motor), o perdedor pode re-tentar mais de uma vez — aceitável e coberto pelo critério 4.
- **Assunções pendentes do Gate 0 (matriz A2):** (a) nome/localização do schema do kernel (`kernel.` assumido) e se a solution será re-estruturada (matriz preliminar aponta reescrever a solution na Fase 0 — esta spec assume código novo no kernel, sem aproveitar middleware legado, se existir); (b) mecânica exata de exposição do ambiente da credencial no `HttpContext` (contrato da Fase 1); (c) `OrphanTimeout` de 60s deve ser revisitado se o timeout síncrono do Motor mudar de ~5s. Ratificação das D-2026-07-01-* na saída do Gate 0 pode ajustar detalhes do 202.
