# Revisão profunda — Design do produto + Plano-Mestre do MVP

**Data:** 2026-07-01
**Alvo:** `docs/superpowers/specs/2026-07-01-nota-fiscal-hub-produto-design.md` e `docs/superpowers/plans/2026-07-01-nota-fiscal-hub-mvp-plano-mestre.md`
**Status:** Correções aplicadas nos dois documentos nesta mesma data; decisões embutidas registradas em `docs/decisions.md` §0, **pendentes de ratificação do dono do produto**.

## Metodologia

5 lentes independentes (consistência interna, correção fiscal/regulatória NFC-e SE, lacunas de produto, viabilidade arquitetural, executabilidade do plano) → 48 achados brutos → verificação adversarial por achado (19 por agentes verificadores; 29 verificados inline contra o texto integral após limite de sessão derrubar os verificadores; o achado regulatório crítico re-confirmado por pesquisa web). **Nenhum achado foi refutado integralmente**; após deduplicação restaram ~24 achados únicos. Decisões fixadas do design §1.1 e handoff §2 não foram reabertas — os achados pedem o detalhe que falta para executá-las.

**Conclusão geral:** documentos estruturalmente sólidos (fronteiras, modelo de módulos e contrato de API se sustentam); a camada fiscal da **contingência** e o **sequenciamento de fases** exigiam revisão antes de detalhar a Fase 3 — aplicada.

## Críticos (3)

### C1 — QR Code prescrito no modelo errado (NT 2025.001)
Design §2.2 e plano Fase 3 fixavam "QR Code com CSC" (v2), sem versão nem `cIdToken`. A NT 2025.001 instituiu o **QR Code v3** (assinatura digital, sem CSC), **obrigatório para NFC-e emitida em contingência** (produção desde 2025) — e contingência é caminho de primeira classe do design (§2.4). Seguir o documento produziria DANFE de contingência com QR inválido.
**Aplicado:** design §2.2 (Motor) — matriz v2/v3 por cenário, cômputo via `GerarQrCode` no módulo de custódia; plano Fase 3. Decisão D-2026-07-01-01.
Fontes: [Tecnospeed](https://blog.tecnospeed.com.br/nota-tecnica-2025-001-nfc-e-qr-code/), [Portal NF-e](https://www.nfe.fazenda.gov.br/portal/exibirArquivo.aspx?conteudo=trSXReoZPuY%3D), [Avalara](https://www.avalara.com/br/pt/blog/2025/09/nt-qrcode-versao3-nfe-nfce.html), [GSoft](https://gsoft.com.br/artigos/nota-tecnica-2025-001).

### C2 — Contingência fiscalmente sub-especificada
`tpEmis` compõe a chave de acesso: entrar em contingência exige **regerar o XML (tpEmis=9 + `dhCont`/`xJust`), nova chave, reassinatura** — nada disso constava (o §3.4 devolvia "chave" no 202 como se fosse a mesma). No caso "enviou e não ouviu", a via tpEmis=1 pode ter sido autorizada → **dois documentos para a mesma venda** (risco nº 1 do §6, sem tratamento). Prazo legal de **24h** para transmitir a contingência ausente ("re-transmite até autorizar" sem teto). DANFE de contingência tem exigências próprias (2 vias, dizeres).
**Aplicado:** design §2.4, §3.4, §3.5, §4.1 (chave original registrada na nota), §4.3 (alerta 24h), §6; plano Fase 3 e Global Constraints. Decisão D-2026-07-01-02.

### C3 — Numeração transacional irreconciliável como escrita
"Alocação dentro da própria transação com lock pessimista" + "Rejeitada libera o número" + "persiste o resultado da SEFAZ na própria transação" não fecham juntos: ou o lock atravessa ~5s de I/O da SEFAZ (serializa a série; colapsa sob SEFAZ degradada), ou a alocação commita antes e "Rejeitada libera" exige um pool não previsto (decrementar = duplicidade). Critério da Fase 3 "zero duplicata/buraco" inatingível sem essa decisão — que o Gate 0 já devolvera como pendência (sequence vs ContadorNumeracao).
**Aplicado:** design §2.2 (Emissão) — transação curta com commit antes da transmissão, pool de números liberados por rejeição, buraco residual → inutilização até o dia 10; plano Fase 3. Decisão D-2026-07-01-04 (ContadorNumeracao mantido; sequence do g0-escalabilidade descartada — ratificar).

## Altos (4)

### A1 — Máquina de estados incompleta no ramo de contingência
`EmContingencia` só saía para `Autorizada` (ou `Cancelada` — fiscalmente impossível sem `nProt`); sem estado/webhook para contingência **rejeitada/denegada na regularização** (consumidor já saiu com o DANFE); "re-transmite até autorizar" contradiz a regra do Motor ("só FalhaTransitória autoriza retry"); estado `Transmitida` não existia em mais nenhum lugar.
**Aplicado:** design §2.2 — `EmContingencia → Transmitida → Autorizada | RejeitadaAposContingencia | Denegada`; `Cancelada` só de `Autorizada`; webhook `nota.contingencia_rejeitada` (§3.6); §3.5, §4.4, §6; plano Fase 3. Decisão D-2026-07-01-03.

### A2 — DANFE e links síncronos vs pipeline assíncrono + fases invertidas
201 com links `xml`/`danfe` de objetos que o Worker só grava depois (janela de 404 sem semântica); 202 com "DANFE pronta para imprimir" sem dono/formato (Documentos está fora do caminho crítico); renderização de DANFE na Fase 4, exigida pelo entregável da Fase 3.
**Aplicado:** design §3.4 — 202 devolve dados estruturados de impressão (PDV imprime; sem PDF síncrono); links do 201 = endpoints do hub (`/xml` serve da Emissão desde o 201; `/danfe` 202 Retry-After até o S3); plano Fases 3/4. Decisão D-2026-07-01-05.

### A3 — Pré-requisitos externos SEFAZ-SE sem dono nem fase
Certificado A1 de teste, CSC/idCSC de homologação, credenciamento na SEF-SE têm lead time burocrático e travariam a Fase 3; equivalentes de produção travariam a Fase 7.
**Aplicado:** plano — checklist do Gate 0 (iniciar em paralelo) + pré-requisito nomeado da Fase 7.

### A4 — Encerramento/offboarding de conta sem decisão
Guarda legal de 5 anos, exportação de dados e destruição do A1/CSC de ex-cliente sem política — primeiro churn viraria crise jurídico-operacional.
**Aplicado:** design §5 — política mínima (somente-leitura por 5 anos; destruição auditada de A1/CSC; exportação em massa explicitamente fora do MVP). Decisão D-2026-07-01-08.

## Médios (aplicados)

| Achado | Aplicação |
|---|---|
| CSC "nunca sai" (§2.3.6) × Motor "consome CSC via contrato" (§2.2/§2.5) | `GerarQrCode` no módulo de custódia; §2.2/§2.3.6/§2.5 e Fase 2 alinhados |
| `tpAmb` escalar × configs por ambiente | §3.7: ambiente **corrente** (escalar) + CSC/séries/contadores por ambiente p/ virada homolog→produção (D-09) |
| `Outbox`/`Inbox` só no schema `emissao` (§4.1) × outbox POR MÓDULO (§2.3.2) | Convenção declarada em §4.1: par padrão de todo schema de módulo |
| `indSinc=1` e `cStat` do `protNFe` não prescritos (bug #1 do código repetível) | §2.2 (Motor) e plano Fase 3 explicitam |
| Prazo de cancelamento sem valor | §2.2: NFC-e 30 min, parametrizado por UF+modelo; extemporâneo fora do MVP (§5) |
| Endpoints cancelamento/inutilização órfãos de fase | Plano Fase 3 passa a incluí-los |
| Fase 1 "backoffice API" sem mecanismo + bootstrap sem dono | Plano Fase 1: autenticação interina declarada (dívida com quitação na Fase 6) + seed/bootstrap |
| 429/"política da conta" × soft enforcement; `ContaSuspensa` indefinida | §2.4/§3.4: MVP sempre emite com `avisos` (429 pós-MVP); `ContaSuspensa` = 403, bloqueia escrita, mantém leitura (D-06) |
| Usuários do Portal (convite/MFA/reset/papéis) sem fase | Plano Fase 6 nomeia os itens |
| Backup/DR/staging indefinidos; "homologação" ambígua | §4.5: PITR + teste de restore (restore × ContadorNumeracao); topologia = 1 ambiente produtivo, homologação = tpAmb=2 (D-07); plano Fase 7 |
| Stack do frontend não decidida | Registrada como decisão em aberto (decisions.md); gate antes de detalhar a Fase 6 |
| Observabilidade toda na Fase 7 | Plano Fase 0: fundação no kernel (logs estruturados, CorrelationId via outbox, OTel); Fase 7 só métricas de negócio/dashboards |
| Infra AWS (KMS/S3 Object Lock) necessária nas Fases 2/4 | Plano Fase 0: provisionamento mínimo como trilha paralela |
| `leitura`/`NotaConsulta` sem módulo dono; entrega de webhook sem dono | §4.1: propriedade da Emissão; §2.2: entrega de webhooks = Contas & Planos, executada no Worker (D-10) |
| Catálogo de planos sem dimensões/ciclo (PARCIAL) | Decisão em aberto (antes da Fase 1) |
| Nenhum canal de notificação humana (PARCIAL) | Decisão em aberto (Fase 6/7) — persona "farmácia sem TI" não consome webhook |
| `EmpresaLocal` × fail-fast síncrono (PARCIAL) | §2.2 precisado: dados via read model; operações de custódia sempre síncronas via contrato |

## Baixos (aplicados ou registrados)

- **LGPD** — parágrafo adicionado ao design §5 (hub = operador; base legal fiscal; DPA pré-requisito da Fase 5).
- **Webhooks pós-24h e ordering** — §3.6: at-least-once sem ordem garantida; entrega `esgotada` + redisparo; endpoint morto desativado com notificação.
- **Inutilização** — prazo (dia 10 do mês subsequente) no design §2.2; detector de lacunas no plano Fase 4.
- **Idempotência com dois donos** — §2.2: mecanismo é do kernel; Emissão apenas o exige.
- **"Ninguém → Contas & Planos"** — §2.5 precisado (nenhum módulo de negócio; host/kernel via contrato público; exceção codificada no NetArchTest).
- **Correções fiscais #1–#3 + cIdToken + NU1903** — itens nomeados da Fase 3 no plano.
- **Destino da solution legada** — decisão em aberto (saída do Gate 0).
- **Numeração como "decisão em aberto" no Gate 0** — resolvida por D-2026-07-01-04 (ratificar na saída do Gate 0).

## Refutados

Nenhum. Dos 19 achados verificados por agentes: 10 CONFIRMADOS, 9 PARCIAIS (núcleo válido com exagero/cobertura parcial), 0 REFUTADOS. Os 29 restantes foram verificados inline com o mesmo protocolo (evidência textual, cobertura em outra seção, exclusão consciente do §5, decisão fixada, correção factual) — vereditos incorporados acima.

## Crítico de completude (o que a revisão não cobriu)

1. O plano-mestre não tem noção de esforço/duração por fase — deliberado (planos bite-sized por fase), mas sem gatilho de replanejamento definido.
2. Obtenção dos pacotes de schemas XSD oficiais para os golden files — cabe no item de pré-requisitos SEFAZ do Gate 0.
3. Custo operacional AWS (KMS por assinatura, S3 Object Lock) nunca estimado — não bloqueia o MVP.

## Decisões em aberto (dono do produto)

Ver `docs/decisions.md` §0: ratificação das decisões D-2026-07-01-01..10; e-mail transacional no MVP; stack do Portal/Backoffice; QR v3 também no online; catálogo de planos (dimensões/ciclo); destino da solution legada.
