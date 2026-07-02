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

---

## Gate 0 — Análise do código existente (ANTES de detalhar qualquer fase)

O repositório já contém implementação prévia (fases 1–16 documentadas em `docs/specs/` e `docs/superpowers/plans/`: domain, infra de banco, integração SEFAZ, certificados/outbox, NFSe ABRASF, cancelamento, docker, testes). Antes de detalhar a Fase 0:

- [ ] Analisar arquitetura, escalabilidade e qualidade do código existente **contra o design aprovado**.
- [ ] Produzir matriz por módulo do design: **APROVEITAR** (já conforme) / **ADAPTAR** (conforme com ajustes) / **REESCREVER** (não conforme às fronteiras) — com justificativa.
- [ ] Decidir estratégia: evoluir o repo atual in-place vs. re-estruturar solution — decisão registrada em `docs/decisions.md`.
- [ ] Ajustar este plano-mestre com o resultado (fases podem encolher muito se houver reaproveitamento).

**Critério de saída:** matriz aprovada pelo dono do produto.

---

## Fase 0 — Fundação e kernel transversal

**Entregável testável:** solution compila com estrutura de módulos, CI verde com testes de arquitetura travando fronteiras, e a biblioteca outbox/inbox provada com duplicata e reordenação.

- Estrutura da solution: `Api/`, `Worker/`, `Modules/<Modulo>/{Domain,Application,Infrastructure,Contracts}`, `BuildingBlocks/` (kernel).
- Tenant context: `ITenantContext`, resolução na borda (middleware), filtro global por `conta_id` em DbContext base, `BeginTenantScope` para o Worker; teste de arquitetura: entidade tenant-scoped sem `conta_id` falha o build.
- Biblioteca Outbox/Inbox por módulo: publicação transacional, dispatcher in-process por módulo, dedupe por `messageId`; testes com entrega duplicada e fora de ordem.
- Middleware de `Idempotency-Key` (armazenamento por conta+key+hash do payload; replay devolve resposta original; conflito → 409).
- Auditoria append-only alimentada por eventos de integração.
- Suite NetArchTest: referências permitidas entre módulos (§2.5 do spec), DTOs nos contratos, proibição de entidade cruzando fronteira.
- CI: build → testes unidade+arquitetura → integração (Testcontainers).

## Fase 1 — Contas & Planos

**Entregável testável:** conta criada via backoffice API, API key emitida/rotacionada/revogada com escopo de empresas, autenticação funcionando na API, plano atribuído e consumo agregado por evento.

- Entidades: Conta, UsuarioPortal (identidade separada), ApiKey (hash, prefixo live/test, allowlist de empresas), Plano, AssinaturaConta, ConsumoPeriodo, WebhookConfig (config apenas; entrega na Fase 4).
- Pipeline de autenticação: resolve key uma vez na borda → `conta_id` + escopo de empresas no contexto.
- Metering: handler de `NotaAutorizada`/`NotaCancelada`/`DocumentoArmazenado` (consome eventos que passarão a existir nas fases 3–4; testado com eventos sintéticos).
- Flags replicáveis: eventos `CotaExcedida`/`ContaSuspensa` publicados quando o agregado cruza limite.

## Fase 2 — Empresas & Certificados

**Entregável testável:** empresa cadastrada com `tpAmb`, upload de A1 validado (senha, titularidade CNPJ, validade), XML de teste assinado via `AssinarXml` sem a chave sair do módulo, alerta de expiração emitido.

- Entidades: Empresa, Certificado (PFX cifrado com envelope encryption/KMS, DEK por empresa), CscConfig (cifrado, por ambiente), SerieConfig.
- Contratos: `AssinarXml(empresaId, xml)`, `CertificadoValido(empresaId)`, leitura de CSC (para o Motor).
- Ciclo de validade: job do Worker publica `CertificadoProximoDoVencimento` (30/15/7/1 dias) e `CertificadoExpirado`.
- Eventos `EmpresaCriada`/`EmpresaAtualizada`/`EmpresaDesativada` (alimentam read model da Emissão na Fase 3).
- Endpoints: `POST /v1/empresas`, `POST /v1/empresas/{id}/certificado`, `PUT /v1/empresas/{id}/series/{modelo}`, `POST /v1/empresas/{id}/csc` (contrato §3.2 do spec).

## Fase 3 — Emissão + Motor NFC-e (o coração; maior fase)

**Entregável testável:** NFC-e autorizada ponta a ponta no ambiente de **homologação da SEFAZ-SE**, incluindo contingência simulada e cancelamento dentro do prazo.

- Máquina de estados completa (`Rascunho→EmProcessamento→Autorizada|Rejeitada|Denegada|EmContingencia→…`), com `Denegada` consumindo número e `Rejeitada` liberando.
- `ContadorNumeracao` com alocação na transação (teste de concorrência: N emissões paralelas, zero duplicata/buraco).
- Read model `EmpresaLocal` alimentado pelos eventos da Fase 2.
- Motor NFC-e: montagem do XML (layout SE), QR Code com CSC, assinatura via contrato da Fase 2, transporte SOAP SEFAZ-SE, classificação por `cStat` (`Autorizada|RejeiçãoDefinitiva|FalhaTransitória|Denegada`), `ConsultarStatus`.
- Golden files de XML validados contra XSD; SEFAZ fake para integração (autorização, rejeição, timeout).
- Contingência (`tpEmis=9`): decisão na Emissão, DANFE de contingência, regularização pelo Worker, reconciliação por consulta no caso ambíguo.
- Eventos fiscais: cancelamento (prazo legal validado), inutilização de faixa.
- Endpoint mínimo `POST /v1/nfce` + `GET /v1/nfce/{id}` (contrato §3 do spec) para fechar o ciclo.

## Fase 4 — Documentos, read model de consulta e webhooks

**Entregável testável:** fluxo completo — nota autorizada → XML/DANFE no S3 → listagem em 1 query → webhook assinado entregue com retry.

- Guarda S3 (SSE-KMS, versionamento, Object Lock, retenção ≥ 5 anos, por ambiente), hash SHA-256, URLs pré-assinadas com revalidação de tenant.
- Renderização DANFE NFC-e (PDF).
- Projeção `NotaConsulta` + `GET /v1/notas` com filtros e paginação.
- Entrega de webhooks: HMAC + timestamp, retry com backoff ~24h, histórico `WebhookEntrega`, validação anti-SSRF no registro.

## Fase 5 — Hardening da API pública v1

**Entregável testável:** API publicável para o primeiro integrador externo — OpenAPI completa, RFC 7807 em todos os erros, rate limiting, `GET /v1/consumo`, coleção de testes de contrato.

## Fase 6 — Portal do Emissor + Backoffice

**Entregável testável:** a "farmácia sem TI" opera sozinha — onboarding, certificado com banner de expiração, séries/CSC, consulta/download/cancelamento, consumo, webhooks; backoffice separado (MFA + RBAC) habilita contas e planos.

## Fase 7 — Observabilidade e go-live

**Entregável testável:** produção com o primeiro tenant piloto (VISU) emitindo NFC-e real em SE.

- Métricas/alertas do §4.3 do spec (contingência agora = alerta nº 1), dashboards, CorrelationId ponta a ponta.
- Deploy AWS (2 containers, RDS, S3, KMS), pipeline com smoke de homologação SEFAZ-SE.
- Checklist go-live (skill go-live-checklist) + runbook de incidente fiscal.

---

## Dependências e sequência

```
Gate 0 → Fase 0 → Fase 1 → Fase 2 → Fase 3 → Fase 4 → Fase 5 → Fase 7
                                              └→ Fase 6 (paralelo a 5) ─┘
```

Fases 1–2 podem se sobrepor parcialmente (contratos da Fase 0 estabilizados). A Fase 3 é o caminho crítico e não começa sem a Fase 2 entregue (assinatura).

## Fora deste plano (fases futuras do produto)

NFSe (Motor NFSe + RPS), NFe, DF-e, importação de XML, Cadastros Fiscais, exportação contábil, billing automático — cada uma nascerá como novo plano-mestre curto + planos de fase, sobre as fronteiras já reservadas no design.
