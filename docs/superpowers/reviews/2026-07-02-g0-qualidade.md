# g0-qualidade — 5º parecer do Gate 0 (qualidade da suíte de testes legada)

> **Data:** 2026-07-02 · **Modelo:** Opus 4.8 · **Método:** análise SÓ-LEITURA, inline com verificação adversarial por achado (fallback previsto na spec A1; ultracode desligado — sem fan-out de subagentes).
> **Card:** https://app.clickup.com/t/86e24c1b5 · **Spec:** `docs/superpowers/specs/tarefas/A1-g0-qualidade.md`
> **Escopo:** código legado `VisuFiscalHub.{Api,Application,Domain,Infrastructure}` + `tests/VisuFiscalHub.Tests`. Nenhum arquivo de `src/` ou `tests/` foi modificado.

Este é o 5º e último parecer do Gate 0. Os quatro anteriores (aderência, fiscal, escalabilidade, segurança) estão consolidados em `docs/superpowers/handoffs/2026-07-02-gate0-handoff.md` §3 — **este relatório não os repete**; referencia-os onde a suíte confirma ou contradiz seus achados.

---

## 0. Resumo executivo

A suíte compila e passa **761/761 em 14s** e cobre bem o **domínio** (máquina de estados, prazos, value objects, taxonomia de `cStat`). Mas **suíte verde ≠ suíte boa**: os mesmos 761 testes conviveram com os 3 bugs fiscais críticos que impedem qualquer emissão real hoje (§3 do handoff), e a razão é estrutural — **a suíte não valida XML contra XSD da SEFAZ, não exercita a semântica transacional/concorrente em banco real, e mascara o descarte silencioso de 5 eventos de domínio**.

Parecer da suíte: **APROVEITAR os testes de domínio e do Motor (porte cirúrgico junto com o código que cobrem); REESCREVER a estratégia de integração e golden files na solution nova.** Coerente com a matriz preliminar (Motor ADAPTAR + testes; kernel REESCREVER).

**Achados novos desta análise** (não cobertos pelos 4 pareceres anteriores):
- **Q1 (ALTO):** 5 eventos de domínio publicados no outbox são **descartados sem efeito** por falta de handler (MSG0005); 3 deles significam que o integrador **nunca é notificado** de rejeição, cancelamento ou falha de emissão — só do caminho feliz.
- **Q2 (ALTO):** ausência de validação XSD nos golden files é a causa-raiz de os bugs fiscais #1 (cStat) e #2 (`<Signature>`) terem passado verdes.
- **Q3 (MÉDIO):** camada "Integração" roda em **EF InMemory + todos os gateways fake**, não em Testcontainers/PostgreSQL — logo não testa numeração transacional, `FOR UPDATE SKIP LOCKED`, nem concorrência.
- **Q4 (BAIXO):** 3 silent failures de baixa severidade em registro auxiliar de `DeliveryAttempt` (deliberados, aceitáveis).

---

## 1. Baseline (evidência datada)

`dotnet test --nologo` em 2026-07-02:

```
Passed!  - Failed: 0, Passed: 761, Skipped: 0, Total: 761, Duration: 14 s - VisuFiscalHub.Tests.dll (net10.0)
```

Contagem de casos `[Fact]`+`[Theory]` (grep, sem `/obj/`): **363 métodos** → 761 execuções (Theories com múltiplos `[InlineData]`). Por camada:

| Camada | Métodos [Fact]/[Theory] |
|---|---|
| Domain | 89 |
| Application | 24 |
| Infrastructure | 180 |
| Integration | 60 |
| Api | 10 |

Avisos de build reproduzidos (todos citados no handoff §4):
- **NU1903 ×2 pacotes** (severidade alta): `System.Security.Cryptography.Xml 10.0.0` (GHSA-37gx-xxp4-5rgx, GHSA-w3x6-4m5h-cxqf — **pacote da assinatura fiscal**) e `Microsoft.OpenApi 2.0.0` (GHSA-v5pm-xwqc-g5wc).
- **MSG0005 ×5** (detalhado em §2).
- **CS0618**: `PerformContext` obsoleto em `CorrelationIdJobFilterTests.cs:49` (só teste).
- **ASPDEPR005**: `ForwardedHeadersOptions.KnownNetworks` obsoleto em `Program.cs:194`.

---

## 2. Silent failures em `src/` (Passo 2)

Padrões pesquisados por grep + leitura de contexto em todo `src/`.

| Padrão | Resultado |
|---|---|
| `.Result` / `.Wait()` / `.GetAwaiter().GetResult()` | **Nenhum encontrado** ✅ |
| `async void` (fora de event handler de UI) | **Nenhum encontrado** ✅ |
| `ContinueWith` sem observar falha / fire-and-forget não aguardado | **Nenhum encontrado** ✅ |
| `catch` genérico/vazio engolindo exceção | 27 `catch` no total; ver abaixo |

O código tem **higiene de erro consistentemente boa**: erros de negócio viram `Result.Failure` explícito ou exceção que dispara retry do Hangfire. Dos 27 `catch`, a maioria esmagadora é **filtrada por tipo** (`when (ex is HttpRequestException …)`) ou **converte em `Result.Failure`** (o falso-positivo que a própria spec manda descartar). Exemplos verificados e **descartados** como silent failure:
- `CertificateEncryptionService.cs:47,71` — `catch → Result.Failure` (correto).
- `TenantCertificateProvider.cs:124,161` — `catch → log + Result.Failure` (correto).
- `CancelamentoJob.cs:119` — `catch → log + registra attempt + throw` (rethrow; Hangfire faz retry — correto).
- `WebhookDeliveryService.cs:213` — `catch` filtrado por exceção transiente + retry agendado (correto).

**Q4 — silent failures reais (severidade BAIXA), 3 ocorrências.** Registro auxiliar de `DeliveryAttempt` engole a própria falha para não reverter a operação principal já concluída:
- `CancelamentoJob.cs:198` — falha ao gravar `DeliveryAttempt` de cancelamento é logada e engolida.
- `WebhookDeliveryService.cs:279` — idem, para attempt de entrega de webhook.
- (o `RegistrarAttemptAsync` do `CancelamentoJob` é o mesmo bloco 184-203.)

Consequência: perda de **telemetria de tentativa** (a operação de negócio em si não é afetada). É uma decisão defensável — falhar no log de auditoria não deve reverter um cancelamento confirmado na SEFAZ — mas o design novo (auditoria append-only, Fase 0/kernel) deve tratar isso como escrita que **não pode** falhar silenciosamente. Registrado, não corrigido.

---

## 3. Os 5 eventos MSG0005 (Passo 3) — achado Q1 (ALTO)

**Mecânica confirmada.** Nenhum handler de evento de domínio é registrado manualmente (`Application/DependencyInjection.cs` só adiciona o Mediator + validators); o Mediator gera os handlers por source-gen e emite **MSG0005** para todo evento sem handler. Existem apenas **2 handlers** no código (`grep INotificationHandler` em `src/`): `DocumentoFiscalAutorizadoEventHandler` (dispara **webhook** ao integrador) e `DocumentoFiscalDenegadoEventHandler` (só loga `Critical`).

**O que o `OutboxRelayJob` faz com um evento sem handler** (`OutboxRelayJob.cs:60-104`): `ResolveEventType` retorna o tipo normalmente (é `INotification` válido — `:137`), o payload desserializa, e `_publisher.Publish(domainEvent)` (`:84`) é chamado. O Mediator, sem handler registrado, faz **no-op silencioso** (não lança). O fluxo cai direto para `:98` — `message.ProcessedAt = now; SaveChanges()` — e a mensagem é **marcada como processada sem nenhum efeito**. Não fica pendente, não marca erro: some silenciosamente.

| Evento | Publicador (arquivo:linha) | Comportamento do OutboxRelay | Consequência de negócio | Classificação |
|---|---|---|---|---|
| `DocumentoFiscalRejeitadoEvent` | `DocumentoFiscal.cs:255` (`Rejeitar`) | marcado processado, no-op | **Integrador não recebe webhook de rejeição** — só descobre se consultar o status ativamente | Funcionalidade silenciosamente ausente (ALTO) |
| `DocumentoFiscalCanceladoEvent` | `DocumentoFiscal.cs:298` (`ConfirmarCancelamento`) | marcado processado, no-op | **Integrador não recebe webhook de cancelamento confirmado** | Funcionalidade silenciosamente ausente (ALTO) |
| `DocumentoFiscalFalhouEvent` | `DocumentoFiscal.cs:329` (`Falhar`) — inclui o `Falhar()` da reconciliação (`ReconciliacaoJobProcessor.cs:187`) | marcado processado, no-op | **Integrador não recebe webhook de falha de emissão** (nota travou e desistiu) | Funcionalidade silenciosamente ausente (ALTO) |
| `ClienteAppWebhookSecretRotadoEvent` | `ClienteApp.cs:89` (`AtualizarWebhookSecret`) | marcado processado, no-op | Rotação de secret já é persistida sincronamente no próprio comando; o evento não teria efeito colateral óbvio | Inofensivo (o evento parece vestigial) |
| `TenantProvisionadoEvent` | `Tenant.cs:77` (`Criar`) | marcado processado, no-op | Nenhum setup assíncrono pós-provisionamento existe no MVP legado | Inofensivo por ora (vira lacuna se o hub passar a fazer setup assíncrono no onboarding) |

**Assimetria central:** o hub notifica o integrador quando a nota é **autorizada**, mas **silencia** quando ela é **rejeitada, cancelada ou falha**. Para um produto cujo contrato é "emita e receba o resultado por webhook", isso é meia-entrega do fluxo de eventos. Não é bug fiscal (a nota em si transita de estado corretamente no banco), mas é **funcionalidade de integração faltante e mascarada** — o `MSG0005` é a única pista, e sem CI ninguém o vê.

---

## 4. Cobertura real vs design §4.4 (Passo 4)

Tabela item a item das 5 linhas do §4.4 (versão revisada em 2026-07-01). Legenda: **COBERTO** / **PARCIAL** / **AUSENTE** / **N/A-herdado** (feature que o design introduziu e não existe no legado — não conta como falha da suíte).

### Linha "Unidade"

| Item do §4.4 | Status | Evidência |
|---|---|---|
| Máquina de estados — Autorizado / Rejeitado / Cancelado / Falhou / Denegado | **COBERTO** | `DocumentoFiscalTests.cs` (transições + publicação de eventos, ex. `:67,120,140`); `DocumentLifecycleTests.cs:159` |
| Estados `EmContingencia` / `RejeitadaAposContingencia` | **N/A-herdado** | Contingência não existe no legado (`tpEmis` hardcoded — handoff §3.2) |
| `Denegada` | **COBERTO** | `DocumentoFiscalTests.cs:67,120`; `DocumentLifecycleTests.cs:159-172` (cStat=110) |
| Numeração sob concorrência (incl. pool de rejeitadas) | **AUSENTE** | Numeração testada só via `FakeSequenceManager` (sequencial, in-memory); nenhum `Task.WhenAll`/`Parallel.For`; `xunit.runner.json` até desabilita paralelização. Pool de rejeitadas não existe no legado (numeração não libera número em Rejeitada — handoff §3.2) |
| Prazo cancelamento 30 min (NFC-e) / 24h (NFe) | **COBERTO** | `DocumentoFiscalTests.cs:473-525` (dentro/fora, ambos os tipos); `CancelarDocumentoCommandHandlerTests.cs:78` |
| Prazo inutilização (dia 10) | **AUSENTE** | Inutilização não existe no legado (handoff §3.2/§3.3) |
| Classificação pelo `cStat` do `protNFe` (não do lote) | **PARCIAL / bug** | `SefazRetornoParserTests.cs` testa as funções de taxonomia isoladas (`IsDuplicidade`/`IsDenegado`/`IsNaoEncontrado` — corretas), mas **nenhum teste alimenta um envelope síncrono real** e verifica extração do `protNFe/infProt/cStat`. Por isso o **bug #1** (`SefazRetornoParser.cs:51` lê o cStat do nível errado) passou verde |

### Linha "Golden files" — **AUSENTE** (achado Q2, ALTO)

| Item do §4.4 | Status | Evidência |
|---|---|---|
| XML do Motor validado contra **XSD da SEFAZ** | **AUSENTE** | **Zero arquivos `.xsd`** no projeto de testes (`glob tests/**/*.xsd` → nada). As únicas ocorrências de "xsd" são strings de namespace ABRASF. Toda verificação de XML é por XPath/string (`SelectSingleNode`) — nunca contra schema |
| Assinatura: `<Signature>` irmã de `infNFe` | **AUSENTE / bug** | `XmlSignerTests.cs` verifica C14N, SignatureMethod, URI e `CheckSignature` (cripto), mas usa `//ds:Signature` (`:131`) — casa em **qualquer profundidade**; nunca afirma que a Signature é filha de `<NFe>` e irmã de `infNFe`. Por isso o **bug #2** (`XmlSigner.cs:51-55`, Signature dentro de `infNFe`) passou verde. Ironia: `NfceXmlBuilderTests.cs:92,97` **sabe** testar posicionamento (`infNFeSupl` na posição certa e ausência na errada) — o padrão existe, mas não foi aplicado à Signature |
| Contingência (`tpEmis=9`, `dhCont`/`xJust`) e QR v2/v3 nos golden files | **N/A-herdado** | Contingência e QR v3 não existem no legado |

**Nexo causal.** Os bugs fiscais #1 e #2 (handoff §3.2) são exatamente o que golden files validados contra XSD teriam pego: um XML com Signature na posição errada **falha o XSD** (rejeição 215); um parser que lê o cStat do lote em vez do protocolo **diverge de um golden de retorno síncrono real**. A suíte verde deu falsa confiança — este é o argumento mais forte contra "APROVEITAR a suíte como está".

### Linha "Arquitetura" — **AUSENTE**

| Item do §4.4 | Status | Evidência |
|---|---|---|
| NetArchTest (regras de fronteira §2.3) | **AUSENTE** | Nenhum uso de NetArchTest/`Types.InAssembly` na suíte (grep vazio). Confirma handoff §3.1 ("SEM NetArchTest"). Esperado: o legado é solution 4-camadas, não o monólito modular do design — as fronteiras a testar nascem na Fase 0 |
| Filtro de tenant em todo DbContext / `conta_id` obrigatório | **AUSENTE** | Nenhum `HasQueryFilter` no código (handoff §3.1/§3.3: isolamento 100% manual). Não há teste de arquitetura que trave isso |

### Linha "Integração" — **PARCIAL** (achado Q3, MÉDIO)

| Item do §4.4 | Status | Evidência |
|---|---|---|
| Fluxo completo com **banco real** (Testcontainers) + SEFAZ fake | **PARCIAL** | `VisuFiscalHubFactory.cs:85-86,106-110` usa **EF InMemory**, não PostgreSQL; `ISefazClient`, `IDocumentJobQueue`, `ICancelamentoJobQueue`, `ISequenceManager` são **todos fakes**. É integração HTTP-in-memory de contrato/estado — não a integração transacional que o §4.4 pede |
| Autorização / rejeição por cStat / timeout→contingência / regularização / reconciliação | **PARCIAL** | Autorização, rejeição e denegação cobertas (`DocumentLifecycleTests`, `DocumentStatusTests`, `IssueDocumentTests`); timeout→contingência **N/A-herdado**; reconciliação testada em unidade (`ReconciliacaoJobProcessorTests`) com transições simuladas por reflexão, não fluxo real |
| Handlers com **eventos duplicados / fora de ordem** | **AUSENTE** | Nenhum teste de dedupe/reordenação (grep `reorder`/`fora de ordem`/`duplicad` só acha idempotência de emissão por header, não inbox de eventos). Não há Inbox no legado (handoff §3.1) |
| Isolamento de tenant (conta A jamais lê dado da conta B) | **PARCIAL** | Cobre **controle de acesso na borda**: `DocumentStatusTests.cs:124,145` e `CancelamentoTests.cs:126` retornam `403 Forbidden` quando um tenant pede documento de outro. **Não** cobre isolamento no nível de query (o design exige `HasQueryFilter`; com InMemory + sem filtro global, um bug de escopo em query nova não seria pego) |

### Linha "E2E/smoke" — **AUSENTE**

| Item do §4.4 | Status | Evidência |
|---|---|---|
| Emissão real em homologação SEFAZ-SE (`tpAmb=2`) no pipeline de release | **AUSENTE** | Não existe pipeline (`.github/` sem workflows — handoff §3.4); SEFAZ é sempre fake. Esperado no legado; nasce na Fase 7 |

---

## 5. Verificação adversarial (Passo 5)

Cada achado foi reverificado contra o código com `arquivo:linha` conferido. Contagem:

- **Achados brutos levantados:** 4 (Q1 MSG0005, Q2 golden files/XSD, Q3 integração InMemory, Q4 silent failure de attempt) + 12 avaliações de cobertura §4.4.
- **Confirmados:** 4 achados + toda a tabela §4.4 (cada linha com evidência de arquivo de teste presente ou ausência comprovada por grep/glob).
- **Refutados / rebaixados:** vários candidatos a silent failure foram **descartados** por serem `catch→Result.Failure` ou `catch→rethrow` (CertificateEncryptionService, TenantCertificateProvider, o `catch` transiente do WebhookDeliveryService, o `catch+throw` do CancelamentoJob). Nenhum achado entrou no relatório sem conferência de linha.

Verificação específica do Q1 (para não superestimar): confirmei que `OutboxRelayJobExecuteTests.cs` mocka `IPublisher` (`:25`) — logo **não exercita o no-op real do Mediator** — e que `OutboxRelayJobResolveTypeTests.cs` usa justamente `DocumentoFiscalAutorizadoEvent` (`:10`), o único evento **com** handler, como "tipo válido". Ou seja, a lacuna de teste que deixou o descarte silencioso passar é dupla e está comprovada, não inferida.

---

## 6. Notas por camada (Passo 6)

Critérios: assertividade dos asserts, isolamento, cobertura dos caminhos de erro, aderência ao §4.4, flakiness aparente. Escala 0-10 medindo **o que os testes afirmam**, não só se passam.

| Camada | Nota | Justificativa |
|---|---|---|
| **Domain (89)** | **8/10** | A camada mais forte. Máquina de estados, prazos (24h/30min por tipo), value objects (CNPJ, ChaveAcesso com DV módulo 11), taxonomia de cStat e publicação de eventos são testados com asserts específicos e isolados, sem I/O. Perde pontos por não cobrir numeração sob concorrência (limitação legítima de teste de unidade puro) e por eventos de domínio serem verificados como *levantados* (`DomainEvents.OfType<…>`) sem que ninguém teste que eles são *consumidos*. |
| **Application (24)** | **7/10** | Handlers e validators com bom isolamento (NSubstitute); idempotência de emissão e prazo de cancelamento cobertos. Enxuta e correta, mas fina para a superfície de comandos/queries; não cobre os caminhos de evento (nenhum teste de handler de notificação além do implícito). |
| **Infrastructure (180)** | **6/10** | A maior camada e a de qualidade mais desigual. Excelente no que testa por unidade: parsers de retorno, builders de XML por XPath, assinatura (cripto), SSRF/HMAC de webhook, criptografia de certificado, jobs (isolamento de falha do OutboxRelay, reconciliação). Mas os asserts de XML são **estruturais por string, nunca contra XSD** — e é exatamente essa lacuna que deixou os bugs fiscais #1 e #2 passarem verdes. Uma suíte de 180 testes que não pega Signature na posição errada nem cStat do nível errado está medindo a coisa errada nos pontos que mais importam. |
| **Integration (60)** | **5/10** | Boa cobertura de contrato HTTP, autenticação/JWT, ciclo de vida de status e controle de acesso na borda (403 cross-tenant). Mas roda em **EF InMemory com todos os gateways fake** — não é a integração com banco real que o §4.4 exige, e por construção não pode testar numeração transacional, `FOR UPDATE SKIP LOCKED`, concorrência, nem isolamento de tenant no nível de query. É teste de integração de aplicação, não de infraestrutura. |
| **Api (10)** | **7/10** | Pequena e focada (middleware de CorrelationId, GlobalExceptionHandler, contrato de endpoints). Faz o que se propõe com asserts diretos; superfície pequena porque a maior parte do contrato HTTP é exercida via Integration. |

### Parecer da suíte: **APROVEITAR (domínio + Motor) / REESCREVER (integração + golden files)**

Coerente com a matriz preliminar do Gate 0 (Motor ADAPTAR com porte cirúrgico + testes; kernel REESCREVER). Concretamente:

- **APROVEITAR e portar junto com o código que cobrem:** os testes de **Domain** (máquina de estados, VOs, prazos) e os de **Motor** em Infrastructure (parsers de cStat, builders NFC-e/NFe, assinatura cripto, endpoint resolver) — são o ativo de teste real e acompanham o porte cirúrgico do Motor.
- **REESCREVER na solution nova:**
  - **Golden files com validação XSD** (os XSDs oficiais da SEFAZ como recurso do projeto de teste) — inegociável; é a lacuna que produziu falsa confiança e deixou 3 bugs críticos passarem.
  - **Integração sobre Testcontainers/PostgreSQL** (não InMemory), para exercitar numeração transacional, outbox `SKIP LOCKED`, dedupe de inbox e isolamento de tenant por query filter.
  - **NetArchTest** (não existe) — as fronteiras do monólito modular nascem na Fase 0 e precisam ser travadas no CI.
- **Corrigir como parte do porte (não é tarefa desta análise, mas o teste deve nascer junto):** cStat do `protNFe`, `<Signature>` irmã de `infNFe`, e **handlers para os 3 eventos de resultado** (Rejeitado/Cancelado/Falhou) para fechar o contrato de webhook.

---

## 7. Referências aos pareceres anteriores (sem duplicação)

- **g0-aderencia** (handoff §3.1): "SEM NetArchTest", "SEM Inbox", "isolamento manual" — confirmados pela ausência de testes correspondentes (§4 linhas Arquitetura e Integração).
- **g0-fiscal** (handoff §3.2): os 3 bugs críticos (cStat, Signature ×2) — este relatório fornece a **evidência de teste** de por que passaram verdes (§4 golden files, achado Q2).
- **g0-escalabilidade** (handoff §3.3): numeração sem escopo/transação, webhook inline no relay — a suíte não os exercita (numeração via fake sequencial; integração InMemory).
- **g0-seguranca** (handoff §3.4): "não existe CI/CD" — confirmado; é o motivo de a suíte ter chegado a não-compilar sem ninguém ver (handoff §4) e de o MSG0005 nunca ter sido investigado.

---

## 8. Insumo para a matriz final do Gate 0

Este parecer fecha os 5 relatórios. Para a matriz (tarefa A2), a contribuição de qualidade é:

| Dimensão | Veredito de qualidade |
|---|---|
| Testes de Domínio | **APROVEITAR** (portar com o domínio) |
| Testes do Motor (parsers/builders/assinatura) | **APROVEITAR** (portar com o Motor) |
| Golden files / validação XSD | **REESCREVER** (ausente; causa-raiz dos bugs fiscais) |
| Testes de Integração | **REESCREVER** (InMemory → Testcontainers) |
| Testes de Arquitetura (NetArchTest) | **CRIAR** (não existe) |
| Contrato de webhook (eventos de resultado) | **CORRIGIR** (3 eventos sem handler — Q1) |

**Riscos residuais registrados, não corrigidos** (regra SÓ-LEITURA do gate): Q1 (eventos descartados), Q2 (sem XSD), Q3 (integração InMemory), Q4 (attempt engolido), NU1903 no pacote de assinatura, ASPDEPR005 em `Program.cs:194`.
