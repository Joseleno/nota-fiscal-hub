# Handoff — Revisão dos documentos CONCLUÍDA + correções aplicadas; falta g0-qualidade e matriz final do Gate 0

**Data:** 2026-07-01
**Como retomar:** `/resume gate 0` — ler este arquivo primeiro. **Este é o handoff mais recente**: o arquivo `2026-07-02-gate0-handoff.md` é da sessão ANTERIOR (a data no nome dele está adiantada); use-o apenas para os 4 relatórios do Gate 0 (seções 3–4 dele) — a seção 8 dele já resume esta sessão.

---

## 1. O que esta sessão fez (concluído; NÃO commitado — ver seção 5)

1. **Suíte de testes destravada** (com ok do dono): `tests/VisuFiscalHub.Tests/Api/GlobalExceptionHandlerTests.cs` corrigido (construtor ganhou `IHostEnvironment`; substitute adicionado). Resultado: **761/761 testes verdes em 16s**. NU1903 confirmados no build: `Microsoft.OpenApi 2.0.0` e `System.Security.Cryptography.Xml 10.0.0` (pacote da assinatura fiscal).
2. **Revisão profunda de design + plano-mestre** (pedido do dono, anterior à conclusão do Gate 0): 5 lentes (consistência, fiscal, lacunas, viabilidade, executabilidade) + verificação adversarial por achado + crítico de completude. 48 achados brutos → ~24 únicos; **0 refutados**. 19 verificados por agentes; 29 verificados inline (limite de sessão derrubou os verificadores); o crítico regulatório (QR v3) confirmado por pesquisa web. Relatório completo: `docs/superpowers/reviews/2026-07-01-revisao-design-plano-mestre.md`.
3. **Correções aplicadas nos dois documentos** (aprovadas pelo dono: "aplique as correções"):
   - **Design** — máquina de estados completa (`EmContingencia → Transmitida → Autorizada | RejeitadaAposContingencia | Denegada`; `Cancelada` só de `Autorizada`); numeração com transação curta + pool de rejeitadas + inutilização de buracos residuais; contingência regera XML (`tpEmis=9`, `dhCont`/`xJust`, nova chave, reassinatura, QR v3) com reconciliação da chave original e prazo de 24h; QR v2/v3 (NT 2025.001) via `GerarQrCode` no módulo de custódia (CSC não transita); `indSinc=1` + decisão pelo `cStat` do `protNFe`; 202 com dados estruturados de impressão (sem PDF síncrono); semântica dos links do 201; `ContaSuspensa`=403 e cota sempre com avisos; tpAmb = ambiente corrente; Outbox/Inbox por módulo; `leitura` da Emissão; webhooks at-least-once sem ordem + política pós-retry; política mínima de encerramento de conta + LGPD; backup/PITR + topologia de ambiente única; 2 riscos novos no §6.
   - **Plano-mestre** — Gate 0 ganha pré-requisitos SEFAZ-SE (certificado teste, CSC homolog, credenciamento, XSDs) e registro de decisões; Fase 0 ganha observabilidade fundacional + provisionamento AWS; Fase 1 ganha autenticação interina declarada + seed/bootstrap; Fase 3 ganha bugs #1–#3 + cIdToken + NU1903 como tarefas nomeadas, contingência completa e endpoints de cancelamento/inutilização; Fase 4 tira PDF do caminho crítico + detector de lacunas; Fase 6 ganha gestão de usuários + gate de stack; Fase 7 ganha backup/restore e smoke tpAmb=2.
4. **Decisões registradas em `docs/decisions.md` §0** (nova seção no topo; seções 1–8 = legado histórico): **D-2026-07-01-01..10**, aplicadas mas **pendentes de ratificação do dono** na saída do Gate 0. Decisões em aberto listadas lá: e-mail transacional no MVP; stack do Portal/Backoffice; QR v3 também no online; catálogo de planos (dimensões/ciclo); destino da solution legada.
5. **Regra de processo registrada na memória** (correção do dono nesta sessão): em fase de validação de documentos/gate, operar SÓ-LEITURA — bloqueios de diagnóstico são apresentados ao dono com opções, nunca corrigidos por conta própria.

## 2. Decisões fixadas (não reabrir)

- Todas da seção 2 do handoff anterior (motor próprio; roadmap NFC-e SE → NFSe → NFe; conta 1..N empresas; API+Portal; síncrono c/ contingência; planos+metering soft; monólito modular 5 módulos+kernel; 2 deployables).
- As D-2026-07-01-01..10 estão APLICADAS nos documentos; não reverter sem o dono — mas apresentá-las para ratificação formal na decisão do Gate 0.

## 3. Estado do Gate 0 (revisão do CÓDIGO contra o design)

- **4/5 relatórios entregues** (aderência, fiscal, escalabilidade, segurança) — consolidados nas seções 3–5 do handoff anterior. Matriz preliminar: reestruturar solution nova (Fase 0) com porte cirúrgico do Motor (`Infrastructure/Fiscal/{Sefaz,XmlBuilder}` + `QrCodeGenerator` + testes); 3 bugs fiscais críticos no código (cStat do nível errado, `<Signature>` dentro de `infNFe`, idem no evento de cancelamento).
- **Falta o 5º (g0-qualidade)** — ver seção 4.

## 4. PRÓXIMO PASSO IMEDIATO — concluir g0-qualidade (só análise/leitura)

Dados já apurados:
- Suíte **compila e passa: 761/761 em 16s** (após o fix da seção 1.1).
- 53 arquivos, 363 `[Fact]`+`[Theory]`: Domain 89, Infrastructure 180, Integration 60, Application 24, Api 10.
- NU1903 ×2 (acima). Avisos do build a investigar como pista de silent failure: **MSG0005 — 5 eventos de domínio SEM handler registrado** (`ClienteAppWebhookSecretRotadoEvent`, `DocumentoFiscalCanceladoEvent`, `DocumentoFiscalFalhouEvent`, `DocumentoFiscalRejeitadoEvent`, `TenantProvisionadoEvent`) — o que o OutboxRelay faz com eles? CS0618 (`PerformContext` obsoleto em teste), ASPDEPR005 (`KnownNetworks` em `Program.cs:194`).

O que falta fazer:
1. **Varredura de silent failures**: catch genérico/vazio engolindo exceção, `.Result`/`.Wait()`/`async void`, `ContinueWith` sem observar falha, jobs/handlers que logam e seguem — com `arquivo:linha`.
2. **Cobertura real vs §4.4 do spec (JÁ REVISADO nesta sessão — usar a versão atual)**: concorrência de numeração? golden files XML/XSD (incl. posicionamento de `<Signature>`)? `cStat` do `protNFe`? isolamento de tenant? eventos duplicados/fora de ordem? prazos? (contingência não existe no código — registrar como N/A herdado).
3. **Nota 0-10 por camada** + parecer APROVEITAR/ADAPTAR/REESCREVER da suíte (alimenta a matriz).

## 5. Working tree NÃO commitada (commitar só se o dono pedir)

```
 M docs/superpowers/handoffs/2026-07-02-gate0-handoff.md      (seção 8 adicionada)
 M docs/superpowers/plans/2026-07-01-nota-fiscal-hub-mvp-plano-mestre.md
 M docs/superpowers/specs/2026-07-01-nota-fiscal-hub-produto-design.md
 M tests/VisuFiscalHub.Tests/Api/GlobalExceptionHandlerTests.cs
?? docs/decisions.md                                          (untracked; §0 novo + legado)
?? docs/superpowers/reviews/                                  (relatório da revisão)
?? docs/superpowers/handoffs/2026-07-01-revisao-docs-e-gate0-handoff.md (este arquivo)
?? docs/implementation-plan.md, docs/specs/fase-*.md, docs/superpowers/plans/2026-05-*.md (legado untracked, já estava assim)
```

## 6. Sequência restante (ordem)

1. Concluir **g0-qualidade** (seção 4) → 2. **Matriz final do Gate 0** (5 pareceres) + ratificação das D-2026-07-01-* → apresentar ao dono para **decisão formal** (critério de saída do Gate 0) → 3. Registrar estratégia + decisões em aberto em `docs/decisions.md` §0 → 4. Ajuste residual do plano-mestre (a maior parte já foi aplicada nesta sessão) → 5. Detalhar a **Fase 0** com superpowers:writing-plans.

## 7. Observações operacionais

- **Limite de sessão da API** bloqueou subagentes até 23:10 de 2026-07-01 (America/Fortaleza) — ao retomar depois disso, workflows voltam a funcionar; antes disso, análise inline funciona normalmente.
- Achados completos da revisão (com justificativas dos verificadores): journal do workflow `wf_0d518f53-776` na pasta de subagents da sessão `0fd3acfb-...` (descartável; o relatório em `docs/superpowers/reviews/` é a fonte persistida).
- Fontes da NT 2025.001 (QR v3) citadas no relatório persistido.
