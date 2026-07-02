# Gate 0 — Matriz final APROVEITAR / ADAPTAR / REESCREVER (pacote de decisão)

> **Data:** 2026-07-02 · **Modelo:** Opus 4.8 · **Tarefa:** A2 (card https://app.clickup.com/t/86e24c1b8) · **Spec:** `docs/superpowers/specs/tarefas/A2-matriz-gate0.md`
> **Natureza:** processo/análise SÓ-LEITURA. Nenhum código de `src/` ou `tests/` foi alterado.
> **Status do gate:** ✅ **APROVADO PELO DONO em 2026-07-02** — Gate 0 FECHADO. As 10 decisões D-2026-07-01-* foram ratificadas integralmente e o destino da solution foi decidido (solution nova). Registro em `docs/decisions.md` §0.1 (D-2026-07-02-01..03).

Consolida os **5 pareceres** do Gate 0 na matriz por componente do design. Entradas:
- **g0-aderencia, g0-fiscal, g0-escalabilidade, g0-seguranca** — `docs/superpowers/handoffs/2026-07-02-gate0-handoff.md` §3 (e matriz preliminar §5).
- **g0-qualidade (A1)** — `docs/superpowers/reviews/2026-07-02-g0-qualidade.md` (produzido nesta sequência).

Vínculos que toda classificação respeita: **Global Constraints** do plano-mestre (`docs/superpowers/plans/2026-07-01-nota-fiscal-hub-mvp-plano-mestre.md` §"Global Constraints") e regras de fronteira §2.3 do design. Uma peça que viole constraint estrutural de forma não-localizada **não pode** ser APROVEITAR.

---

## 1. Matriz final

Critérios aplicados (o primeiro que falha rebaixa o parecer):
- **APROVEITAR** = (a) nenhum achado crítico/alto incide + (b) não viola constraint/fronteira + (c) porte mecânico + (d) testes existentes seguem válidos.
- **ADAPTAR** = (a) núcleo correto e validado por ≥1 relatório + (b) ajustes enumeráveis com correção conhecida + (c) custo do ajuste < reescrita (comparação explícita).
- **REESCREVER** = componente não existe **ou** viola constraint de forma não-localizada **ou** ajuste ≥ reescrita **ou** ≥2 relatórios o classificam REESCREVER sem núcleo aproveitável.
- **Desempate:** divergência → prevalece o parecer mais conservador, salvo contra-evidência concreta.

| # | Componente do design | Parecer | Justificativa (fonte + arquivo:linha) | Ações de porte (origem → destino) | Esforço | Fase | Δ vs preliminar |
|---|---|---|---|---|---|---|---|
| 1 | **Contas & Planos** | **REESCREVER** | Só existe `ClienteApp` ≈ Conta (fração pequena; sem planos, metering, cota, MFA) — g0-aderencia §3.1. Não há como APROVEITAR nem ADAPTAR o que não foi construído. | Modelar Conta + catálogo de planos + metering soft desde o zero no módulo Contas & Planos. `ClienteApp` do legado vira insumo de mapeamento (client_id/secret/webhook-config), não código portado. | G | 1 | Confirma preliminar |
| 2 | **Empresas & Certificados — estrutura/agregados** | **ADAPTAR** (com quebra de agregado) | `Tenant` funde Empresa+Certificado+CSC+Série com nomenclatura invertida (Tenant deveria ser a Conta) — g0-aderencia §3.1. O mapeamento de dados fiscais (CNPJ, `ConfiguracaoFiscal`, endereço, `CIdToken`) é correto e coberto por testes de domínio (g0-qualidade: Domain 8/10). Ajuste = desmembrar o agregado; < reescrever a modelagem fiscal inteira. | Separar Empresa (dados cadastrais/fiscais) de Certificado/CSC/Série; renomear conceitos. Portar VOs fiscais (`Cnpj`, `ChaveAcesso`, `ConfiguracaoFiscal`) e seus testes. Legado `Domain/Entities/Tenant.cs` → módulo Empresas & Certificados. | G | 1–2 | Confirma preliminar |
| 3 | **Empresas & Certificados — custódia A1/CSC** | **REESCREVER** | Viola constraint de custódia de forma não-localizada: uma **chave AES estática única** para todos os tenants (`CertificateEncryptionService.cs:16-25`), sem KMS/DEK; chave privada **trafega como `X509Certificate2` entre camadas** (`TenantCertificateProvider`→`XmlSigner`/`SefazHttpClient`) em vez de confinada atrás de `AssinarXml` — g0-seguranca §3.4. Global Constraint: "chave privada nunca sai de Empresas & Certificados". | Redesenhar custódia com KMS + DEK por tenant; confinar a chave atrás de `AssinarXml(...)` (a chave nunca cruza fronteira de módulo). O algoritmo AES-GCM em si (`CertificateEncryptionService`, bem testado — g0-qualidade) é reaproveitável como primitiva interna, mas a arquitetura de custódia é nova. | M | 2 | Confirma preliminar |
| 4 | **Emissão — núcleo (numeração, máquina de estados, síncrono/contingência)** | **REESCREVER** | Todos os 4 relatórios convergem. Fluxo 100% **assíncrono** (endpoints sempre 202, `Program.cs:434-531`) vs. síncrono 201/202/422 do design — g0-escalabilidade §3.3. Contingência **inexiste** (`tpEmis` hardcoded) — g0-fiscal §3.2. Numeração sem escopo `tpAmb`/modelo, não libera número em Rejeitada. São violações estruturais não-localizadas + features ausentes. | Reescrever o núcleo de emissão no módulo Emissão: fluxo síncrono com timeout ~5s, `ContadorNumeracao` transacional (D-2026-07-01-04), contingência completa (D-2026-07-01-02/03), máquina de estados com `EmContingencia`/`RejeitadaAposContingencia`. A máquina de estados **base** do legado (Domain 8/10, exceto contingência) informa o desenho. | G | 3 | Confirma preliminar |
| 5 | **Emissão — casca (endpoints/handlers/idempotência)** | **ADAPTAR** | A idempotência de emissão (index único + handler) e a estrutura CQRS de comandos/queries funcionam e têm testes (g0-qualidade: Application 7/10). Ajustes enumeráveis: idempotência precisa virar middleware por conta+key+hash (Global Constraint) e a casca precisa expor 201/202/422 síncronos. < reescrever a camada de aplicação inteira. | Portar comandos/validators de emissão; elevar idempotência embutida → middleware de `Idempotency-Key` (kernel). Ajustar contrato HTTP para síncrono. `Application/…/Issue*` → módulo Emissão. | M | 3 | Confirma preliminar |
| 6 | **Motor NFCe/NFe (XML, transporte SEFAZ, QR, cStat)** | **ADAPTAR** — **condicionado às correções fiscais** | Maior ativo do legado; núcleo correto e validado (chave+DV módulo 11, taxonomia cStat, `SefazEndpointResolver` SE→SVRS, XML builders) — g0-fiscal §3.2. **Porém** g0-qualidade rebaixa a rede de segurança: os testes de XML **não validam contra XSD** (Q2), e por isso os **3 bugs críticos** passaram verdes. ADAPTAR só se sustenta com as correções + golden files na Fase 3. Ajuste (bugs pontuais + golden files) << reescrever o Motor. | Porte cirúrgico: `Infrastructure/Fiscal/{Sefaz,XmlBuilder}` + `QrCodeGenerator` + testes → módulo Motor. **Ações obrigatórias:** (1) bug #1 cStat do `protNFe` não do lote (`SefazRetornoParser.cs:51`); (2) bug #2 `<Signature>` irmã de `infNFe` (`XmlSigner.cs:51-55`); (3) bug #3 assinatura no evento de cancelamento; (4) `cIdToken` no QR (`QrCode.cs:41,46`); (5) golden files validados contra XSD (não existem — Q2); (6) mitigar NU1903 em `System.Security.Cryptography.Xml`. | M | 3 | **Δ:** preliminar dizia ADAPTAR incondicional; **A1 torna o parecer condicionado a XSD/golden files** — sem eles o "porte + testes" perde a rede e o Motor não pode ser considerado portado. |
| 7 | **Documentos (S3, retenção, read model)** | **REESCREVER** | Não existe: XML embutido no agregado, sem S3, sem read model, sem retenção — g0-aderencia §3.1. Critério REESCREVER (a): componente ausente. | Construir módulo Documentos do zero: guarda em S3, retenção 5 anos, read model de consulta, endpoints `/xml` e `/danfe` (D-2026-07-01-05). | M | 4 | Confirma preliminar |
| 8 | **Kernel — tenant context / idempotência / auditoria / NetArchTest** | **REESCREVER** | Fundação transversal ausente ou não-conforme: **sem filtro global de tenant** (zero `HasQueryFilter`, isolamento 100% manual), **sem auditoria**, **sem NetArchTest**, jobs de varredura sem `BeginTenantScope` — g0-aderencia §3.1 + g0-seguranca §3.4 + g0-escalabilidade §3.3. g0-qualidade confirma pela ausência de testes correspondentes (Q3, Arquitetura AUSENTE). Viola múltiplas Global Constraints de forma não-localizada. | Construir kernel `BuildingBlocks/` na Fase 0: `ITenantContext` + filtro global por `conta_id`, `BeginTenantScope`, middleware de idempotência, auditoria append-only, NetArchTest travando fronteiras. As checagens manuais de tenant do legado permanecem como **defesa em profundidade**, não como mecanismo primário. | G | 0 | Confirma preliminar |
| 9 | **Outbox / Inbox** | **ADAPTAR** | Mecânica de outbox central madura: `DomainEventsInterceptor` + `OutboxRelayJob` com `FOR UPDATE SKIP LOCKED` — g0-aderencia §3.1. Ajustes enumeráveis: (a) **falta Inbox/dedupe**; (b) **webhook entregue inline na transação do relay** (HTTP 15s segurando lock) deve ir para job próprio — g0-escalabilidade §3.3; (c) tornar por-módulo; (d) **handlers ausentes** para 5 eventos (Q1 do A1). Ajuste << reescrever o outbox. | Portar a mecânica do `OutboxRelayJob` → `BuildingBlocks` por módulo; adicionar Inbox idempotente + dedupe por `messageId`; mover entrega de webhook para job próprio; **registrar handlers para `DocumentoFiscalRejeitado/Cancelado/Falhou`** (fecham o contrato de webhook — Q1). Testes de duplicata/reordenação (Global Constraint) — ausentes hoje. | M | 0 | **Δ:** preliminar já dizia ADAPTAR; **A1 acrescenta o achado Q1** (5 eventos sem handler descartados silenciosamente) como ação de porte explícita — o outbox "funciona" mas engole eventos. |
| 10 | **Deployables (API / Worker)** | **REESCREVER** (P/M) | 1 processo único: Hangfire in-process na API (`Program.cs:598`), sem Worker separado — g0-aderencia §3.1 + g0-escalabilidade §3.3. Design exige 2 deployables (API, Worker). Reestruturação de topologia, não ajuste localizado. | Separar em 2 hosts (`Api/`, `Worker/`); filas por criticidade no Worker; `BeginTenantScope` em todo job. Fundação da Fase 0. | P/M | 0 | Confirma preliminar |
| 11 | **Suíte de testes** | **APROVEITAR (domínio + Motor) / REESCREVER (integração + golden files + arquitetura)** — parecer dividido, desmembrado abaixo | Parecer do A1. Domain 8/10 e Motor (parsers/builders/assinatura cripto) são o ativo real e acompanham o porte. Integração roda em **EF InMemory + gateways fake** (5/10, Q3) — não é a integração com banco real do §4.4. Golden files **sem XSD** (Q2). NetArchTest ausente. | **APROVEITAR:** portar testes de Domain + Motor junto com o código que cobrem. **REESCREVER:** golden files com XSD oficial (Fase 3); integração sobre Testcontainers/PostgreSQL (não InMemory); criar NetArchTest (Fase 0). | M | 0–3 | **Δ:** preliminar não tinha linha de suíte (A1 estava pendente). **Nova linha inteira**, com o rebaixamento de Integração e a ausência de XSD como achados-chave. |
| 12 | **NFSe ABRASF** | **NÃO PORTAR agora (fase 2)** | Decisão convergente aderencia+fiscal: scaffold sem assinatura; o módulo Motor NFSe nasce na fase 2 (design §5, fora do MVP). Não é APROVEITAR/ADAPTAR/REESCREVER agora — é **adiar**. | Nenhuma ação no MVP. Registrar o scaffold existente como referência para a fase 2. | — | 2 (pós-MVP) | Confirma preliminar |

### Auto-teste dos critérios (Passo "teste do critério" da spec)

Aplicando os critérios de trás para frente em 2 linhas:

- **Motor (linha 6) = ADAPTAR?** (a) núcleo correto validado por g0-fiscal ✅; (b) ajustes enumeráveis com correção conhecida — os 3 bugs + cIdToken + golden files, todos nomeados ✅; (c) ajuste (bugs pontuais num Motor que já monta XML e fala com SEFAZ) << reescrever motor fiscal do zero ✅. **Sustenta-se, condicionado às correções** — que é exatamente como está classificado.
- **Kernel (linha 8) = REESCREVER?** Basta uma condição: viola constraint estrutural de forma não-localizada (sem filtro de tenant em lugar nenhum; sem auditoria; sem NetArchTest) ✅. Dois+ relatórios classificam REESCREVER (aderência, segurança, escalabilidade) sem núcleo aproveitável ✅. **Sustenta-se.**

### Cobertura (Passo "checklist de completude")

Todas as áreas do design §2.2 + kernel cross-cutting têm linha: Contas&Planos (1), Empresas&Certificados (2,3), Emissão (4,5), Motor (6), Documentos (7), Kernel (8), Outbox/Inbox (9), Deployables (10), Suíte (11), NFSe (12). A área **Auth/JWT/hash/SSRF/logs** da matriz preliminar (APROVEITAR) é absorvida transversalmente: PBKDF2 600k, JWT RS256, anti-SSRF de webhook e logs sem PII (g0-seguranca §3.4 "FORTE (aproveitar)") permanecem e migram como convenção/kernel — sem linha própria por não serem um módulo do design, mas registrados aqui como **APROVEITAR** explícito para não se perderem.

---

## 2. Estratégia geral (convergência dos 5 pareceres)

**Reestruturar em solution nova (Fase 0), com porte cirúrgico do Motor e os padrões do legado (VOs, `Result<T>`, eventos, CQRS, Clean Architecture entre camadas) como convenção.** Não evoluir o repo in-place. Justificativa: 6 das 11 linhas exigem REESCREVER (Contas&Planos, custódia, Emissão-núcleo, Documentos, Kernel, Deployables) por serem ausentes ou violarem constraints de forma não-localizada — evoluir in-place carregaria a god entity `DocumentoFiscal`, o agregado `Tenant` invertido, e a ausência de filtro global de tenant como dívida estrutural. O que se aproveita (Motor, testes de domínio) é **portável** para a solution nova sem perda.

Isto **é** a decisão em aberto "destino da solution legada" (§3 abaixo) e precisa da chancela do dono.

---

## 3. Ratificações pedidas ao dono (critério de saída do gate)

### 3.1 Decisões D-2026-07-01-01..10 — **RATIFICADAS INTEGRALMENTE pelo dono (2026-07-02)**

| ID | Resumo | Ratifica? |
|---|---|---|
| D-2026-07-01-01 | QR v3 obrigatório em contingência; v2 online; cômputo na custódia | ✅ sim |
| D-2026-07-01-02 | Contingência regera XML (`tpEmis=9`), consulta chave original, ≤24h | ✅ sim |
| D-2026-07-01-03 | `RejeitadaAposContingencia` + `Cancelada` só de `Autorizada` | ✅ sim |
| D-2026-07-01-04 | `ContadorNumeracao` transacional (sequence descartado) + pool de rejeitadas | ✅ sim |
| D-2026-07-01-05 | 202 devolve dados de impressão (sem PDF síncrono); links do 201 | ✅ sim |
| D-2026-07-01-06 | Cota excedida sempre emite com avisos; `ContaSuspensa`=403 | ✅ sim |
| D-2026-07-01-07 | Um ambiente produtivo; homologação = `tpAmb=2` | ✅ sim |
| D-2026-07-01-08 | Encerramento: somente-leitura 5 anos + destruição auditada A1/CSC | ✅ sim |
| D-2026-07-01-09 | `tpAmb` = ambiente corrente; CSC/séries/contadores por ambiente | ✅ sim |
| D-2026-07-01-10 | Schema `leitura` na Emissão; webhooks em Contas & Planos via inbox | ✅ sim |

Nenhum veto — design e plano-mestre não precisam de emenda por conta das D-*.

### 3.2 Decisões em aberto (mínimo exigido na saída: destino da solution)

| Decisão | Recomendação da matriz | Decisão do dono |
|---|---|---|
| **Destino da solution legada** (exigida agora) | **Solution nova** (Fase 0) + porte cirúrgico; legado vira referência/origem de porte, não base evolutiva | ✅ **SOLUTION NOVA** (registrada em D-2026-07-02-01) |
| Stack do Portal/Backoffice (antes da Fase 6) | Sem recomendação da matriz — decisão de produto | Adiada para a Fase 6 |
| Catálogo de planos: dimensões/ciclo (antes da Fase 1) | Sem recomendação da matriz — decisão de produto | Adiada para a Fase 1 |
| QR v3 também no online (Fase 3) | Sem recomendação da matriz — decisão fiscal/produto | Adiada para a Fase 3 |
| E-mail transacional no MVP (Fase 6/7) | Sem recomendação da matriz — decisão de produto | Adiada para a Fase 6/7 |

---

## 4. Impacto no plano-mestre (após aprovação)

- **Fase 0 cresce** (confirmado): além do kernel já previsto, absorve a separação de deployables (linha 10) e a mecânica de outbox portada + Inbox (linha 9).
- **Fase 3 encolhe no Motor, cresce em correções**: o Motor é portado (não reescrito), mas ganha como itens nomeados os 3 bugs fiscais + cIdToken + **golden files com XSD** (pré-condição para considerar o porte concluído) + mitigação NU1903.
- **Fase 1** absorve Contas & Planos do zero (linha 1) e a quebra do agregado `Tenant` (linha 2).
- **Fase 2** ganha a custódia KMS/DEK (linha 3).
- **Fase 4** ganha o módulo Documentos do zero (linha 7).
- **Checkboxes do Gate 0** (plano-mestre §"Gate 0") a marcar após aprovação: análise feita ✅, matriz produzida ✅, estratégia decidida (pendente dono), plano-mestre ajustado (pós-aprovação), pré-requisitos SEFAZ-SE iniciados (tarefa A4), decisões registradas (pós-aprovação).

> Os ajustes efetivos em `docs/decisions.md` §0 e no plano-mestre são o **Passo 6 da spec A2** — só executados **após** a aprovação, para não registrar decisão não-chancelada. Nada foi editado neles ainda.

---

## 5. Riscos e pontos de atenção (registrados, não resolvidos)

- **Motor ADAPTAR sem rede**: se os golden files/XSD não forem criados na Fase 3 antes do porte, o Motor herda o risco dos 3 bugs sem cobertura — a ação de porte torna os golden files pré-condição de conclusão.
- **Ratificação parcial das D-***: veto a qualquer decisão exige emenda a design/plano antes de fechar o gate.
- **Working tree com correções não commitadas**: design, plano-mestre e `decisions.md` foram corrigidos em 2026-07-01 e commitados até `a2d2743`; o fix de teste do `GlobalExceptionHandlerTests` foi commitado em `9916468`. A matriz referencia essas versões corrigidas. (Situação já resolvida — não há pendência de commit de artefato-fonte.)
- **Escopo SÓ-LEITURA**: os 3 bugs fiscais são itens da matriz (Fase 3), não correções desta tarefa.

---

## 6. Aprovação do dono do produto (critério de saída do Gate 0)

- **Matriz aprovada integralmente?** ✅ **sim** — sem ressalvas nem reanálises.
- **Ressalvas / reanálises:** nenhuma.
- **Aprovado por:** dono do produto · **Data:** 2026-07-02.

**Gate 0 FECHADO.** Próximos passos: A3 (formalizar a ratificação em governança) e detalhamento da Fase 0 via `superpowers:writing-plans`.
