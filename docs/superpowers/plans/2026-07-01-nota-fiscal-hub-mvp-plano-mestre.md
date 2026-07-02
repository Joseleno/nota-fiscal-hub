# nota-fiscal-hub MVP — Plano-Mestre de Implementação

> **For agentic workers:** Este é o plano-mestre (roadmap de fases). Cada fase será detalhada em seu próprio plano bite-sized (`docs/superpowers/plans/`) via skill superpowers:writing-plans **após o Gate 0** (análise do código existente), e executada com superpowers:subagent-driven-development ou superpowers:executing-plans.

**Goal:** Colocar em produção o MVP do nota-fiscal-hub — emissão de NFC-e em Sergipe via API pública multi-tenant, com portal do emissor — conforme o design aprovado em `docs/superpowers/specs/2026-07-01-nota-fiscal-hub-produto-design.md`.

**Architecture:** Monólito modular .NET com 2 deployables (API + Worker), 5 módulos com fronteiras rígidas (escrita isolada por schema, leitura por read models, outbox por módulo, tenant como fronteira de primeira classe), preparado para extração futura de microsserviços.

**Tech Stack:** .NET, EF Core (schema por módulo), PostgreSQL/RDS, AWS S3 + KMS, xUnit + NetArchTest + Testcontainers, Docker, SEFAZ-SE (homologação e produção).

## Global Constraints (do spec — valem para TODAS as fases)

- Certificado A1 somente; chave privada nunca sai de Empresas & Certificados (`AssinarXml`).
- `conta_id` em toda tabela tenant-scoped + filtro global obrigatório + `BeginTenantScope` em todo job do Worker.
- Outbox POR MÓDULO; handlers idempotentes e tolerantes a reordenação, testados com duplicata/fora-de-ordem.
- IDs `ContaId`/`EmpresaId` são GUIDs opacos do módulo dono; sem FK/join cross-módulo na escrita.
- `tpAmb` permeia numeração, guarda, metering (só produção conta) e escopo de API key (`nfh_test_` não opera produção).
- `Idempotency-Key` obrigatória na emissão; mesma key + payload diferente → 409.
- Eventos de integração carregam identificadores, nunca XML/PDF/PII.
- Erros da API em RFC 7807; logs estruturados sem PII.
- TDD em todas as fases; NetArchTest no CI desde a Fase 0.
- Contingência: novo XML `tpEmis=9` (`dhCont`/`xJust`), chave própria, QR Code v3 (NT 2025.001); transmissão em ≤ 24h com alerta de primeira classe.
- Transporte SEFAZ: `indSinc=1`, lote unitário; a decisão é o `cStat` do `protNFe` (nunca o do lote).

---

## Gate 0 — Análise do código existente (ANTES de detalhar qualquer fase)

O repositório já contém implementação prévia (fases 1–16 documentadas em `docs/specs/` e `docs/superpowers/plans/`: domain, infra de banco, integração SEFAZ, certificados/outbox, NFSe ABRASF, cancelamento, docker, testes). Antes de detalhar a Fase 0:

- [x] Analisar arquitetura, escalabilidade e qualidade do código existente **contra o design aprovado**. — 5 pareceres (aderência, fiscal, escalabilidade, segurança, qualidade); último em `docs/superpowers/reviews/2026-07-02-g0-qualidade.md`.
- [x] Produzir matriz por módulo do design: **APROVEITAR** / **ADAPTAR** / **REESCREVER** — com justificativa. — `docs/superpowers/reviews/2026-07-02-gate0-matriz-final.md`.
- [x] Decidir estratégia: evoluir in-place vs. re-estruturar solution — **SOLUTION NOVA** (D-2026-07-02-01 em `docs/decisions.md` §0.1).
- [ ] Ajustar este plano-mestre com o resultado (fases podem encolher muito se houver reaproveitamento). — *parcialmente refletido nos ajustes de fase abaixo; detalhamento fino na tarefa A5.*
- [ ] Iniciar em paralelo os pré-requisitos externos SEFAZ-SE: certificado A1 de teste, CSC/idCSC de homologação, credenciamento do emissor, pacotes de schemas XSD para golden files (equivalentes de produção = pré-requisito nomeado da Fase 7). — *tarefa A4.*
- [x] Registrar em `docs/decisions.md` as decisões pendentes e ratificar as D-2026-07-01-01..10. — **D-2026-07-01-* ratificadas integralmente** e destino da solution decidido (D-2026-07-02-01); Portal/planos/QR-v3-online/e-mail adiados às fases indicadas.

**Critério de saída:** ✅ **ATINGIDO em 2026-07-02** — matriz aprovada pelo dono do produto e decisões registradas em `docs/decisions.md` §0.1. **Gate 0 FECHADO.**

---

## Fase 0 — Fundação e kernel transversal

**Entregável testável:** solution compila com estrutura de módulos, CI verde com testes de arquitetura travando fronteiras, e a biblioteca outbox/inbox provada com duplicata e reordenação.

- Estrutura da solution: `Api/`, `Worker/`, `Modules/<Modulo>/{Domain,Application,Infrastructure,Contracts}`, `BuildingBlocks/` (kernel).
- Tenant context: `ITenantContext`, resolução na borda (middleware), filtro global por `conta_id` em DbContext base, `BeginTenantScope` para o Worker; teste de arquitetura: entidade tenant-scoped sem `conta_id` falha o build.
- Biblioteca Outbox/Inbox por módulo: publicação transacional, dispatcher in-process por módulo, dedupe por `messageId`; testes com entrega duplicada e fora de ordem.
- Middleware de `Idempotency-Key` (armazenamento por conta+key+hash do payload; replay devolve resposta original; conflito → 409).
- Auditoria append-only alimentada por eventos de integração.
- Suite NetArchTest: referências permitidas entre módulos (§2.5 do spec, incl. a exceção host/kernel → Contas & Planos), DTOs nos contratos, proibição de entidade cruzando fronteira.
- Observabilidade fundacional no kernel: logging estruturado sem PII, `CorrelationId` propagado via outbox, OTel básico (métricas de negócio, alertas e dashboards ficam na Fase 7).
- Provisionamento AWS mínimo (KMS, buckets S3 com Object Lock, RDS de não-produção) como trilha paralela — entregáveis das Fases 2 e 4 dependem dele.
- CI: build → testes unidade+arquitetura → integração (Testcontainers).

## Fase 1 — Contas & Planos

**Entregável testável:** conta criada via backoffice API, API key emitida/rotacionada/revogada com escopo de empresas, autenticação funcionando na API, plano atribuído e consumo agregado por evento.

- Entidades: Conta, UsuarioPortal (identidade separada), ApiKey (hash, prefixo live/test, allowlist de empresas), Plano, AssinaturaConta, ConsumoPeriodo, WebhookConfig (config apenas; entrega na Fase 4).
- Pipeline de autenticação: resolve key uma vez na borda → `conta_id` + escopo de empresas no contexto.
- Metering: handler de `NotaAutorizada`/`NotaCancelada`/`DocumentoArmazenado` (consome eventos que passarão a existir nas fases 3–4; testado com eventos sintéticos).
- Flags replicáveis: eventos `CotaExcedida` (cruzou limite do plano) e `ContaSuspensa` (ação administrativa do backoffice) publicados; semântica de resposta da API em §3.4 do spec.
- API administrativa interina para conta/plano: autenticação de operador provisória declarada (AdminKey de escopo restrito — nunca o pipeline de API key), registrada como dívida com quitação na Fase 6.
- Seed/bootstrap: catálogo de planos, operador inicial, conta piloto (VISU).

## Fase 2 — Empresas & Certificados

**Entregável testável:** empresa cadastrada com `tpAmb`, upload de A1 validado (senha, titularidade CNPJ, validade), XML de teste assinado via `AssinarXml` sem a chave sair do módulo, alerta de expiração emitido.

- Entidades: Empresa, Certificado (PFX cifrado com envelope encryption/KMS, DEK por empresa), CscConfig (cifrado, por ambiente), SerieConfig.
- Contratos: `AssinarXml(empresaId, xml)`, `CertificadoValido(empresaId)`, `GerarQrCode(empresaId, dadosQr)` (hash v2 com CSC / assinatura v3 — CSC não sai do módulo).
- Ciclo de validade: job do Worker publica `CertificadoProximoDoVencimento` (30/15/7/1 dias) e `CertificadoExpirado`.
- Eventos `EmpresaCriada`/`EmpresaAtualizada`/`EmpresaDesativada` (alimentam read model da Emissão na Fase 3).
- Endpoints: `POST /v1/empresas`, `POST /v1/empresas/{id}/certificado`, `PUT /v1/empresas/{id}/series/{modelo}`, `POST /v1/empresas/{id}/csc` (contrato §3.2 do spec).

## Fase 3 — Emissão + Motor NFC-e (o coração; maior fase)

**Entregável testável:** NFC-e autorizada ponta a ponta no ambiente de **homologação da SEFAZ-SE**, incluindo contingência simulada e cancelamento dentro do prazo.

- Máquina de estados completa (`Rascunho→EmProcessamento→Autorizada|Rejeitada|Denegada|EmContingencia`; `EmContingencia→Transmitida→Autorizada|RejeitadaAposContingencia|Denegada`; `Cancelada` só de `Autorizada`), com `Denegada` consumindo número e `Rejeitada` devolvendo ao pool do contador.
- `ContadorNumeracao` com alocação em transação curta (commit antes de transmitir) + pool de números liberados por rejeição (teste de concorrência: N emissões paralelas com rejeições intercaladas — zero duplicata; buraco só por crash, detectado para inutilização).
- Read model `EmpresaLocal` alimentado pelos eventos da Fase 2.
- Motor NFC-e: montagem do XML (layout SE), QR Code v2/v3 via `GerarQrCode` (contrato da Fase 2), assinatura via contrato da Fase 2, transporte SOAP SEFAZ-SE (`NFeAutorizacao4`, `indSinc=1`), classificação pelo `cStat` do `protNFe` (`Autorizada|RejeiçãoDefinitiva|FalhaTransitória|Denegada`), `ConsultarStatus`.
- Itens fiscais do Gate 0 como tarefas nomeadas com teste de aceite: decisão pelo `cStat` do `protNFe` (bug #1), `<Signature>` irmã de `infNFe` (bug #2) e posicionamento correto no evento (bug #3), `cIdToken` no QR v2; upgrade/mitigação de `System.Security.Cryptography.Xml` (NU1903) junto do porte da assinatura.
- Golden files de XML validados contra XSD — incl. contingência (`tpEmis=9`, `dhCont`/`xJust`) e QR v2/v3; SEFAZ fake para integração (autorização, rejeição, timeout, regularização rejeitada).
- Contingência completa: regeração do XML (`tpEmis=9`, `dhCont`/`xJust`, nova chave, reassinatura, QR v3), consulta da chave original antes de transmitir (caso ambíguo), prazo de 24h com alerta, 202 com dados estruturados de impressão (sem PDF síncrono).
- Eventos fiscais: cancelamento (30 min parametrizado por UF+modelo), inutilização de faixa (prazo dia 10 validado).
- Endpoints `POST /v1/nfce`, `GET /v1/nfce/{id}`, `POST /v1/nfce/{id}/cancelamento` e `POST /v1/inutilizacoes` (contrato §3 do spec) para fechar o ciclo.

## Fase 4 — Documentos, read model de consulta e webhooks

**Entregável testável:** fluxo completo — nota autorizada → XML/DANFE no S3 → listagem em 1 query → webhook assinado entregue com retry.

- Guarda S3 (SSE-KMS, versionamento, Object Lock, retenção ≥ 5 anos, por ambiente), hash SHA-256, URLs pré-assinadas com revalidação de tenant.
- Renderização DANFE NFC-e (PDF) para guarda/download — a impressão no PDV usa os dados estruturados do 201/202 (sem PDF no caminho crítico).
- Detector de lacunas de numeração no Worker com alerta antes do prazo de inutilização (dia 10 do mês subsequente).
- Projeção `NotaConsulta` + `GET /v1/notas` com filtros e paginação.
- Entrega de webhooks: HMAC + timestamp, retry com backoff ~24h, histórico `WebhookEntrega`, validação anti-SSRF no registro.

## Fase 5 — Hardening da API pública v1

**Entregável testável:** API publicável para o primeiro integrador externo — OpenAPI completa, RFC 7807 em todos os erros, rate limiting, `GET /v1/consumo`, coleção de testes de contrato.

## Fase 6 — Portal do Emissor + Backoffice

**Entregável testável:** a "farmácia sem TI" opera sozinha — onboarding, certificado com banner de expiração, séries/CSC, consulta/download/cancelamento, fila de tratamento de contingência rejeitada, consumo, webhooks; gestão de usuários da conta (convite, reset de senha, MFA, papel por empresa); backoffice separado (MFA + RBAC) habilita contas e planos, substituindo a autenticação interina da Fase 1.

- Stack do Portal/Backoffice definida em `docs/decisions.md` antes do detalhamento desta fase.

## Fase 7 — Observabilidade e go-live

**Entregável testável:** produção com o primeiro tenant piloto (VISU) emitindo NFC-e real em SE.

- Métricas/alertas do §4.3 do spec (contingência agora e a vencer 24h = alerta nº 1), dashboards de negócio (fundação de observabilidade vem da Fase 0), CorrelationId ponta a ponta verificado.
- Deploy AWS de produção (2 containers, RDS, S3, KMS — não-produção provisionada desde a Fase 0), pipeline com smoke de homologação SEFAZ-SE (empresas `tpAmb=2`; pré-requisito: credenciamento de produção iniciado no Gate 0).
- Backup/restore: RDS PITR com RPO/RTO alvo e teste de restore no runbook (restore × `ContadorNumeracao`: reconciliar por `ConsultarStatus` antes de reabrir emissão).
- Checklist go-live (skill go-live-checklist) + runbook de incidente fiscal (incl. contingência rejeitada e prazo de 24h).

---

## Dependências e sequência

```
Gate 0 → Fase 0 → Fase 1 → Fase 2 → Fase 3 → Fase 4 → Fase 5 → Fase 7
                                              └→ Fase 6 (paralelo a 5) ─┘
```

Fases 1–2 podem se sobrepor parcialmente (contratos da Fase 0 estabilizados). A Fase 3 é o caminho crítico e não começa sem a Fase 2 entregue (assinatura).

## Fora deste plano (fases futuras do produto)

NFSe (Motor NFSe + RPS), NFe, DF-e, importação de XML, Cadastros Fiscais, exportação contábil, billing automático — cada uma nascerá como novo plano-mestre curto + planos de fase, sobre as fronteiras já reservadas no design.
