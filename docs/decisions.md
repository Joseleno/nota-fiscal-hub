# VisuFiscalHub — Decisões de Arquitetura e Requisitos de Negócio

**Versão:** 3.0
**Data:** 2026-05-11
**Responsável:** Time VisuFiscalHub

---

## 0. Decisões do produto nota-fiscal-hub (revisão de documentos, 2026-07-01)

> As seções 1–8 abaixo documentam o **VisuFiscalHub legado** (implementação prévia ao design de produto de 2026-07-01) e permanecem como referência histórica. Onde conflitarem com o design aprovado (`docs/superpowers/specs/2026-07-01-nota-fiscal-hub-produto-design.md`), **o design prevalece**.

Decisões embutidas na revisão profunda de documentos de 2026-07-01 (relatório: `docs/superpowers/reviews/2026-07-01-revisao-design-plano-mestre.md`), aplicadas ao design e ao plano-mestre. **Ratificadas pelo dono do produto na sessão de saída do Gate 0** (ver §0.2).

Status possíveis: `Ratificada` / `Ratificada-com-ajuste` / `Revogada` / `Proposta`.

| ID | Decisão | Racional | Status | Data | Decisor |
|---|---|---|---|---|---|
| D-2026-07-01-01 | QR Code conforme NT 2025.001: **v3 obrigatório em contingência**, v2 (`cIdToken`+CSC) no online; cômputo via `GerarQrCode` no módulo de custódia | v3 é obrigatório para contingência desde 2025; CSC não sai da custódia (regra de fronteira 6) | Ratificada | 2026-07-02 | Joseleno D. M. dos Santos (dono) |
| D-2026-07-01-02 | Contingência **regera o documento** (novo XML `tpEmis=9` + `dhCont`/`xJust`, nova chave, reassinatura); no "enviou sem resposta", consultar a chave original antes de transmitir — se autorizada, cancelá-la (a via entregue ao consumidor prevalece); transmissão ≤ 24h com alerta | `tpEmis` compõe a chave; evita documento duplicado (risco nº 1 do §6) e infração de prazo legal | Ratificada | 2026-07-02 | Joseleno D. M. dos Santos (dono) |
| D-2026-07-01-03 | Máquina de estados ganha `RejeitadaAposContingencia` (incidente com fila de tratamento + webhook `nota.contingencia_rejeitada`); `Cancelada` só a partir de `Autorizada` | Contingência rejeitada é cenário real com DANFE já entregue; cancelamento exige `nProt` | Ratificada | 2026-07-02 | Joseleno D. M. dos Santos (dono) |
| D-2026-07-01-04 | Numeração: `ContadorNumeracao` transacional **mantido** (proposta de sequence do g0-escalabilidade descartada), com transação curta (commit antes de transmitir) + pool de números liberados por rejeição; buraco residual → inutilização até o dia 10 | Lock nunca atravessa I/O; "Rejeitada libera" vira mecanismo implementável sem duplicidade | Ratificada | 2026-07-02 | Joseleno D. M. dos Santos (dono) |
| D-2026-07-01-05 | 202 de contingência devolve **dados estruturados de impressão** (payload + QR v3 + dizeres; PDV imprime 2 vias) — sem PDF síncrono; links do 201 são endpoints do hub (`/xml` serve da Emissão; `/danfe` `202 Retry-After` até o S3) | Documentos permanece fora do caminho crítico; elimina a janela de 404 | Ratificada | 2026-07-02 | Joseleno D. M. dos Santos (dono) |
| D-2026-07-01-06 | Cota excedida no MVP **sempre emite com `avisos`** (429 é pós-MVP); `ContaSuspensa` = `403 conta_suspensa`, bloqueia escrita, mantém leitura/download | Alinha o contrato §3.4 ao soft enforcement fixado em §1.1.7/§2.4 | Ratificada | 2026-07-02 | Joseleno D. M. dos Santos (dono) |
| D-2026-07-01-07 | Topologia: **um ambiente produtivo do hub**; "homologação" do pipeline = empresas `tpAmb=2` (sem staging separado no MVP) | Custo/simplicidade; `tpAmb` já é de primeira classe | Ratificada | 2026-07-02 | Joseleno D. M. dos Santos (dono) |
| D-2026-07-01-08 | Encerramento de conta (política mínima): somente-leitura por 5 anos (guarda legal), destruição auditada de A1/CSC, exportação em massa fora do MVP | Guarda legal + custódia de material sensível de ex-cliente | Ratificada | 2026-07-02 | Joseleno D. M. dos Santos (dono) |
| D-2026-07-01-09 | `tpAmb` é o **ambiente corrente** (escalar) da empresa; CSC/séries/contadores mantidos por ambiente para a virada homologação→produção | Resolve a ambiguidade do modelo (§3.7/§4.1) | Ratificada | 2026-07-02 | Joseleno D. M. dos Santos (dono) |
| D-2026-07-01-10 | Schema `leitura`/`NotaConsulta` pertence à **Emissão**; entrega de webhooks é responsabilidade de **Contas & Planos**, executada no Worker via inbox | Todo schema precisa de módulo dono (regra §2.3.1) | Ratificada | 2026-07-02 | Joseleno D. M. dos Santos (dono) |

**Decisões em aberto — governança** (nenhuma pendente sem dono e prazo):

| ID | Decisão | Dono | Prazo/Fase-limite | Status |
|---|---|---|---|---|
| P1 | Destino da solution legada | Joseleno D. M. dos Santos (dono) | Saída do Gate 0 | **Decidida** → solution nova (D-2026-07-02-01) |
| P2 | Stack do Portal/Backoffice | Joseleno D. M. dos Santos (dono) | Antes de detalhar a Fase 6 | Pendente |
| P3 | Catálogo de planos — dimensões de limite e ciclo | Joseleno D. M. dos Santos (dono) | Antes da Fase 1 | Pendente |
| P4 | E-mail transacional no MVP (persona "farmácia sem TI") | Joseleno D. M. dos Santos (dono) | Fase 6/7 | Pendente |
| P5 | Adoção do QR v3 também no online | Joseleno D. M. dos Santos (dono) | Fase 3 | Pendente |

### 0.1 Saída do Gate 0 — decisões novas (2026-07-02)

Gate 0 concluído: 5 pareceres (aderência, fiscal, escalabilidade, segurança, qualidade) consolidados na matriz final `docs/superpowers/reviews/2026-07-02-gate0-matriz-final.md`, **aprovada pelo dono do produto em 2026-07-02**. Decisões novas registradas nesta saída:

| ID | Decisão | Racional | Status | Data | Decisor |
|---|---|---|---|---|---|
| D-2026-07-02-01 | **Estratégia — destino da solution legada: SOLUTION NOVA.** Criar solution modular na Fase 0 (5 módulos + kernel `BuildingBlocks/`, 2 deployables API/Worker, NetArchTest no CI); **portar cirurgicamente** o Motor (`Infrastructure/Fiscal/{Sefaz,XmlBuilder}` + `QrCodeGenerator`, ~2.654 linhas) + testes de domínio; padrões do legado (VOs, `Result<T>`, eventos, CQRS, Clean Architecture entre camadas) viram convenção. O legado `VisuFiscalHub.*` fica em `old/` como referência/origem de porte, **não** base evolutiva. **Racional ancorado na matriz A2** (`docs/superpowers/reviews/2026-07-02-gate0-matriz-final.md`). | 6 das 12 linhas da matriz são REESCREVER por ausência ou violação estrutural não-localizada (sem filtro global de tenant, chave privada entre camadas, fluxo 100% assíncrono, god entity `DocumentoFiscal`, agregado `Tenant` invertido, 1 deployable). A forma-alvo do design (monólito modular) é incompatível com a forma atual (4 camadas, 1 deployable, DbContext único); evoluir in-place remontaria a topologia carregando a dívida estrutural sem NetArchTest como rede durante a migração. Convergência dos 5 pareceres. | Ratificada | 2026-07-02 | Joseleno D. M. dos Santos (dono) |
| D-2026-07-02-02 | **Motor NFCe/NFe = ADAPTAR condicionado.** O parecer ADAPTAR do Motor está condicionado a, na Fase 3, criar **golden files validados contra XSD oficial da SEFAZ** antes de considerar o porte concluído, e corrigir os 3 bugs fiscais + `cIdToken`. | g0-qualidade (A1) mostrou que a suíte não valida XML contra XSD (achado Q2), e é por isso que os 3 bugs críticos passaram verdes — sem golden files o "porte + testes" perde a rede de segurança. | Ratificada | 2026-07-02 | Joseleno D. M. dos Santos (dono) |
| D-2026-07-02-03 | **Contrato de webhook: registrar handlers para `DocumentoFiscalRejeitado/Cancelado/Falhou`** ao portar o outbox (Fase 0/kernel). | g0-qualidade (A1) achado Q1: hoje esses 3 eventos são levantados, viram outbox e são **descartados sem efeito** (MSG0005) — o integrador só é notificado do caminho feliz (Autorizado). | Ratificada | 2026-07-02 | Joseleno D. M. dos Santos (dono) |

### 0.2 Sessão de ratificação (evidência de governança)

- **Data:** 2026-07-02 · **Decisor:** Joseleno Dias Moreira dos Santos (dono do produto) · **Registro por:** agente (assistente), sem síntese de veredicto.
- **Escopo da sessão:** ratificação item a item das D-2026-07-01-01..10 (todas **Ratificadas**, zero em `Proposta`), decisão de estratégia (**solution nova** — D-2026-07-02-01) e governança das decisões em aberto P1–P5 (P1 decidida; P2–P5 mantidas Pendentes com dono e fase-limite).
- **Revogações:** nenhuma. Portanto **nenhuma pendência de propagação** para design/plano-mestre por conta de reversão de decisão.
- **Critério de saída do Gate 0 (decisões):** ✅ atingido — matriz aprovada e decisões registradas com governança rastreável. Gate 0 FECHADO.

### 0.3 Tarefa A5 — plano-mestre ajustado e plano da Fase 0 aprovados (2026-07-02)

Tarefa A5 concluída (passos 1-4, 6, 7 da spec; passo 5 — sync ClickUp — fora de escopo por decisão do dono, ver handoff 2026-07-02). Escopo:

| ID | Decisão | Racional | Status | Data | Decisor |
|---|---|---|---|---|---|
| D-2026-07-02-04 | **Plano-mestre ajustado com a matriz A2 aprovado; plano bite-sized da Fase 0 aprovado.** `docs/superpowers/plans/2026-07-01-nota-fiscal-hub-mvp-plano-mestre.md` (Gate 0 marcado fechado; Fases 0-4 e "Fora deste plano" ganham blockquote de rastreabilidade citando a linha da matriz que motivou cada ajuste; D-2026-07-01-08 refletida nas Fases 1/2). `docs/superpowers/plans/2026-07-02-fase-00-fundacao-kernel.md` criado — 9 tarefas sequenciadas (B1→B9) cobrindo estrutura da solution, tenant context, outbox/inbox, idempotência, auditoria, NetArchTest, observabilidade, AWS não-produção e CI. Ambos revisados adversarialmente em 2 passadas com Opus 4.8 antes da aprovação (achados críticos e menores corrigidos; dívidas remanescentes registradas nos próprios documentos). | Formaliza a saída do Gate 0 como plano executável, conforme exigido pela spec `docs/superpowers/specs/tarefas/A5-ajuste-plano-pos-gate.md`. | Ratificada | 2026-07-02 | Joseleno D. M. dos Santos (dono) |

### 0.4 Fase 0 em execução — Tarefa 2 (tenant context): decisão arquitetural sobre ciclo de vida de DI

| ID | Decisão | Racional | Status | Data | Decisor |
|---|---|---|---|---|---|
| D-2026-07-02-05 | **`ITenantContext`/`ITenantScopeFactory` (`AmbientTenantContext`) são registrados como singleton no DI dos dois hosts (Api e Worker), nunca scoped.** O isolamento por request/fluxo assíncrono continua garantido pelo `AsyncLocal` interno (mesmo padrão de `System.Transactions.Transaction.Current`), independente do tempo de vida da instância de DI. | O cache de modelo compilado do EF Core (`IModelCacheKeyFactory`) é por TIPO de DbContext, não por instância — corrigido nesta tarefa com `TenantModelCacheKeyFactory` (chave inclui a identidade da instância de `ITenantContext`). Registrar `ITenantContext` como Scoped não reabre uma falha de isolamento (o fix da chave de cache é robusto a múltiplas instâncias), mas reintroduz recompilação de modelo por request (custo de performance/memória) — revisão adversarial confirmou o raciocínio antes da ratificação. | Ratificada | 2026-07-02 | Joseleno D. M. dos Santos (dono) |

### 0.5 Fase 0 em execução — Tarefa 5 (auditoria append-only): retenção da trilha

| ID | Decisão | Racional | Status | Data | Decisor |
|---|---|---|---|---|---|
| D-2026-07-03-01 | **Retenção da trilha de auditoria (`auditoria.registro_auditoria`) ≥ 5 anos, espelhando a guarda fiscal (D-2026-07-01-08). Job de expurgo e particionamento físico da tabela ficam adiados** (fora do MVP — spec B5, Assunção A3: reavaliar particionamento quando eventos de emissão de alto volume entrarem no catálogo, particionar por mês **antes** de reduzir o catálogo se o custo de escrita aparecer). | Trilha de auditoria é evidência de ações administrativas sensíveis (design §2.2) — mesma lógica de retenção de 5 anos já ratificada para guarda fiscal/legal de documentos e para a política de encerramento de conta (D-2026-07-01-08); volume do MVP (NFC-e SE, tenant piloto) dispensa expurgo/particionamento automatizado nesta fase. | Ratificada | 2026-07-03 | Joseleno D. M. dos Santos (dono) |

**Nota de proveniência desta entrada:** o brief da Tarefa 5 instruía adicionar esta decisão em `docs/decisions.md` "§0", citando o esquema de numeração `D-2026-07-01-NN`/"próximo ID sequencial" descrito em `docs/superpowers/specs/tarefas/A3-decisoes-ratificacao.md`. Uma nova verificação neste arquivo (`grep` por `§0` e por `D-2026-07`) confirma que a única seção "0" existente é esta (`## 0. Decisões do produto...`, já numerada em subseções `0.1`–`0.4`, sem literal `§0` no texto) — a Tarefa A3 (ratificação formal de D-2026-07-01-01..10 sob a notação `§0`) não alterou este arquivo até o momento desta tarefa. Optei por seguir o padrão já estabelecido pelas subseções `0.1`–`0.4` (cada uma documentando a decisão de uma tarefa da Fase 0 em execução) e criar `0.5` com o próximo ID sequencial real da tabela (`D-2026-07-03-0N`, primeira entrada com prefixo de data de hoje — não existia nenhuma `D-2026-07-03-*` anterior). Um humano deve corrigir a numeração/posicionamento se a Tarefa A3 for executada depois e definir um esquema diferente.

### 0.6 Fase 0 em execução — Tarefa 9 (CI completo): plataforma do pipeline

| ID | Decisão | Racional | Status | Data | Decisor |
|---|---|---|---|---|---|
| D-2026-07-06-01 | **CI em GitHub Actions, runner `ubuntu-latest` (Docker Linux nativo para Testcontainers).** Workflow único `.github/workflows/ci.yml` com job sequencial restore → build → guard-arquitetura-não-vazia → test-unidade → test-arquitetura → test-integração → artefatos (TRX + cobertura), `timeout-minutes: 20`, cache de NuGet via `packages.lock.json`. | O remoto do repositório já é `https://github.com/Joseleno/nota-fiscal-hub.git` (GitHub) — nenhuma decisão de plataforma nova a tomar, só formalizar. `ubuntu-latest` tem Docker nativo, pré-requisito do estágio de integração (Testcontainers/Postgres) sem exigir runner self-hosted. Job único (não jobs paralelos) evita rebuild redundante entre estágios; a separação fica visível por step nomeado. | Ratificada | 2026-07-06 | Joseleno D. M. dos Santos (dono) |

**Achado relevante registrado nesta tarefa (não é uma decisão, é um estado observado):** ao ligar o estágio `test-integracao` contra a suíte real (`tests/NotaFiscalHub.IntegrationTests`), 35 dos 60 testes falham na branch `dev` de hoje — mesma causa-raiz documentada no relatório da Tarefa 7: (1) `IdempotencyExpirationJob` sem try/catch no loop de polling (Tarefa 4) derruba o host e cascateia falhas em outros testes da mesma collection; (2) `OutboxDispatcher<TDbContext>`/`RetentionCleanupService<TDbContext>` não resolvíveis via `GetRequiredService<T>()` nesta versão de `Microsoft.Extensions.Hosting` (Tarefa 3); (3) `TenantIsolationTests` com expressão LINQ não traduzível pelo EF Core (Tarefa 2); (4) gap de schema/migration na fixture de Auditoria (Tarefa 5). Nenhum desses 4 bugs foi corrigido por esta tarefa (fora de escopo — não são responsabilidade do CI). O `ci.yml` roda a suíte completa, sem filtro/exclusão, exatamente como a spec B9 e o design especificam — isto significa que **hoje o estágio `test-integracao` genuinamente fica vermelho em qualquer push/PR contra `dev`**, não apenas nas provas RED intencionais desta tarefa. Ver relatório da Tarefa 9 (`.superpowers/sdd/task-9-report.md`) para o detalhamento completo e a recomendação de que uma tarefa futura (ou decisão explícita do dono do produto) escolha entre: (a) corrigir os 4 bugs antes de exigir `test-integracao` como check obrigatório; (b) filtrar temporariamente os testes afetados via trait com issue rastreada; ou (c) manter o gate visível mas não bloqueante até a correção. Esta tarefa não tomou essa decisão de escopo unilateralmente.

---

## Sumário

1. [Visão Geral](#1-visão-geral)
2. [Decisões de Arquitetura](#2-decisões-de-arquitetura)
3. [Modelo de Identidade](#3-modelo-de-identidade)
4. [Requisitos de Negócio](#4-requisitos-de-negócio)
5. [Regras de Negócio](#5-regras-de-negócio)
6. [Requisitos Não-Funcionais](#6-requisitos-não-funcionais)
7. [Fora de Escopo](#7-fora-de-escopo)
8. [Decisões de Qualidade e Testabilidade](#8-decisões-de-qualidade-e-testabilidade)

---

## 1. Visão Geral

O **VisuFiscalHub** é um microserviço .NET 10 para emissão de documentos fiscais eletrônicos (NFC-e, NF-e, NFS-e) com integração direta ao SEFAZ, sem dependência de plataformas SaaS intermediárias.

### Objetivo

Centralizar toda a lógica fiscal, tributária e de comunicação com o SEFAZ em um único serviço reutilizável. O sistema cliente (ClienteApp) envia dados de negócio (produtos, valores, pagamentos, consumidor) e recebe o documento fiscal pronto — sem precisar conhecer normas fiscais, schemas XML, chaves de acesso, cálculo de impostos ou QR Code.

### Público-alvo

| Papel | Descrição |
|---|---|
| **ClienteApp** | Sistema de software que integra com o hub via API REST (ex: Visu em PHP). Não precisa conhecer legislação fiscal. |
| **Tenant** | Empresa emissora de notas (ex: barbearia com CNPJ específico). Cadastrada e gerenciada pelo ClienteApp. |
| **Time de Desenvolvimento** | Desenvolvedores que implementam e mantêm o hub. Este documento é a referência primária para tomadas de decisão durante a implementação. |

### Primeiro cliente

O sistema **Visu** (PHP) é o primeiro ClienteApp a integrar com o hub. As decisões foram tomadas levando em conta a necessidade de uma API transparente que isole o Visu de toda a complexidade fiscal.

---

## 2. Decisões de Arquitetura

### DA-01 — Stack tecnológico

| Componente | Decisão | Justificativa |
|---|---|---|
| Runtime | .NET 10 | LTS, performance, ecosistema maduro para fiscal |
| API | ASP.NET Core Minimal APIs | Menos cerimônia que Controllers, melhor para microserviço |
| Arquitetura | Clean Architecture (Domain / Application / Infrastructure / Api) | Separação clara de responsabilidades, testabilidade |
| CQRS | Mediator.SourceGenerator | Source-gen sem reflexão em runtime; evita dependência do MediatR |
| ORM | EF Core 10 + PostgreSQL (Npgsql) | Suporte a tipos PostgreSQL avançados, transações confiáveis |
| Jobs | Hangfire | Retry com backoff, dashboard embutido, persistência no PostgreSQL |
| Testes | xUnit + Shouldly + NSubstitute | Padrão do ecossistema .NET; Shouldly para mensagens legíveis |
| Container | Docker + docker-compose | Portabilidade e paridade entre ambientes |

**Decisão de isolamento de dados:** O isolamento entre ClienteApps depende exclusivamente da camada de aplicação (EF Core filtrando por `clientAppId` extraído do JWT). Não há Row-Level Security no PostgreSQL. Esta é uma decisão consciente: RLS aumentaria a complexidade do schema e do processo de migrations sem benefício proporcional no cenário atual. Operações administrativas diretas no banco devem usar usuário de banco com acesso restrito ao schema. Reavaliar se o número de ClienteApps crescer significativamente.

### DA-02 — Autenticação OAuth2 auto-emitido

**Decisão:** O hub emite seus próprios JWTs via endpoint `/auth/token` (Client Credentials flow). Não há servidor de identidade externo (sem Keycloak, Auth0, etc.). Os tokens são assinados com **RS256** (RSA assimétrico).

**Justificativa:**
- Evita dependência de infraestrutura adicional na fase inicial.
- O fluxo é simples: ClienteApp autentica com `client_id` + `client_secret` e recebe um JWT de vida curta.
- O JWT contém o `client_id`. O `X-Tenant-Id` no cabeçalho identifica o tenant por requisição usando o **UUID interno do Tenant** (não o CNPJ).
- Pode ser substituído por um servidor de identidade externo no futuro sem impacto no domínio.

**Por que RS256 e não HS256:**
Com HS256 e chave simétrica única (`JWT__Secret`), qualquer parte que conhece o segredo pode forjar tokens de qualquer ClienteApp. RS256 com chave privada exclusiva do hub elimina esse risco: a chave privada assina, a chave pública valida — clientes externos podem verificar tokens sem precisar do segredo de assinatura.

**Configuração:**
- `JWT__PrivateKeyPem` — chave privada RSA em formato PEM (variável de ambiente; nunca em código)
- `JWT__PublicKeyPem` — chave pública RSA em formato PEM
- Para rotação zero-downtime de chaves: `JWT__PublicKeyPem` suporta múltiplas chaves via lista indexada (`JWT__PublicKeyPems__0`, `JWT__PublicKeyPems__1`). `TokenValidationParameters.IssuerSigningKeys` recebe a coleção completa — tokens assinados com qualquer chave da lista são aceitos. Procedimento de rotação: adicionar nova chave pública → aguardar expiração de todos os tokens antigos (TTL = 1h) → remover chave antiga.

**Carregamento correto da chave privada RSA (obrigatório):**
```csharp
var rsa = RSA.Create();
rsa.ImportFromPem(privateKeyPem);
var key = new RsaSecurityKey(rsa);
```
`RSA.Create()` seguido de `ImportFromPem` é o único caminho correto. Não usar `X509Certificate2.CreateFromPem` (para certificados, não chaves PEM brutas).

**Por que UUID interno no X-Tenant-Id e não CNPJ:**
CNPJ exposto em headers aparece em todos os logs de proxy/WAF e amplia superfície de ataque. O UUID interno é opaco e sem semântica enumerável — não revela informações sobre a empresa e não pode ser usado para enumerar tenants.

**ICurrentUserContext — chain de população:**
`ICurrentUserContext` é definida na Application layer. A implementação concreta `HttpContextCurrentUserContext` reside na camada `Api/Authentication/`, registrada como `Scoped`. Ela lê `ClienteAppId` dos claims do JWT via `IHttpContextAccessor.HttpContext.User` e `TenantId` do `HttpContext.Items["TenantContext"]` populado pelo `TenantValidationMiddleware`. Handlers do Mediator recebem `ICurrentUserContext` injetado por construtor — **nunca** acessam `IHttpContextAccessor` diretamente na camada Application.

**Fluxo:**

```
POST /auth/token
  Body: { client_id, client_secret, grant_type: "client_credentials" }
  Response: { access_token (JWT RS256), expires_in }

POST /nfce
  Headers: Authorization: Bearer <JWT>
            X-Tenant-Id: <tenant-uuid-interno>
            X-Idempotency-Key: <uuid>
```

### DA-03 — Integração SEFAZ direta via SOAP/HTTPS com mTLS

**Decisão:** Comunicação direta com os webservices do SEFAZ usando SOAP sobre HTTPS com autenticação mTLS (certificado A1 do tenant).

**Justificativa:**
- Elimina custo recorrente de plataformas SaaS (NFe.io, Focus NFe, etc.).
- Controle total sobre o pipeline de emissão, retry e contingência.
- Requisito do cliente: não depender de terceiros para emissão.

**Consequência:** O hub é responsável por gerenciar URLs de webservice por UF e tipo de documento, ambientes (homologação/produção), e versões do schema NF-e.

**Validação do certificado do servidor SEFAZ:** O `SefazHttpClient` deve usar a validação padrão da cadeia de certificação do SO (sem `RemoteCertificateValidationCallback` que retorne `true` incondicionalmente). A cadeia ICP-Brasil deve estar instalada na imagem Docker base — incluir pacote `ca-certificates` com a cadeia ICP-Brasil no `Dockerfile` se necessário. Certificate pinning não é adotado (URLs do SEFAZ mudam em updates de infraestrutura).

**HttpClient via IHttpClientFactory (obrigatório):** O `SefazHttpClient` deve ser registrado via `services.AddHttpClient<SefazHttpClient>()` com `ConfigurePrimaryHttpMessageHandler`. **Nunca** criar `HttpClientHandler` por chamada — causa socket exhaustion em produção.

**HttpClient sem auto-redirect:** `HttpClientHandler.AllowAutoRedirect = false` obrigatório para prevenir bypass de proteção SSRF via redirecionamento HTTP no webhook dispatcher.

### DA-04 — Assinatura XML com RSA-SHA1 + C14N

**Decisão:** Assinatura do XML NFC-e/NF-e usando RSA-SHA1 com canonicalização C14N exclusiva (`http://www.w3.org/TR/2001/REC-xml-c14n-20010315`).

**Justificativa:** O SEFAZ exige SHA1 para assinatura digital da NFC-e/NF-e. SHA256 resulta em rejeição. Esta é uma limitação normativa, não uma escolha de design.

**Alerta de segurança:** SHA1 é considerado fraco para uso geral. No contexto do SEFAZ, é a única opção aceita atualmente. Monitorar NT futuras que possam migrar para SHA256.

### DA-05 — Armazenamento de certificados A1

**Decisão:** Certificados PFX/PKCS#12 armazenados criptografados no banco de dados (PostgreSQL) com AES-256-GCM. Cache em memória com `IMemoryCache` + `SemaphoreSlim` por tenant para evitar decriptografia repetida.

**Justificativa:**
- Certificado por tenant: cada empresa emissora tem seu próprio certificado A1.
- Banco como fonte de verdade simplifica deployments (sem gerenciamento de arquivos em disco).
- Cache por tenant evita overhead de decriptografia em cada requisição.
- SemaphoreSlim previne race condition no warm-up do cache.

**Requisitos obrigatórios de implementação:**
- O **nonce AES-GCM deve ser gerado com `RandomNumberGenerator.GetBytes(12)` a cada chamada de `Encrypt()`** — nunca reutilizado. Reutilização de nonce com AES-GCM quebra a confidencialidade e integridade do ciphertext.
- O carregamento do certificado deve usar `X509CertificateLoader.LoadPkcs12(bytes, pwd, X509KeyStorageFlags.EphemeralKeySet)` — **apenas `EphemeralKeySet`**, não combinado com `MachineKeySet`. Essas flags são mutuamente exclusivas: `EphemeralKeySet` significa "não persistir em nenhum key store" e prevalece, mas combiná-las gera comportamento indefinido em algumas plataformas. Em containers Linux (ambiente de deploy do hub), apenas `EphemeralKeySet` é correto.
- O cache deve armazenar `byte[]` decriptografado (bytes do PFX), **não** o objeto `X509Certificate2`. Armazenar `X509Certificate2` em cache de longa duração causa memory leak de handles nativos do SO. O objeto `X509Certificate2` deve ser criado na hora do uso e descartado com `using`.
- O campo `CertificadoVencimento` (DateTime) deve ser armazenado no Tenant para monitoramento de expiração e alertas preventivos.

**Risco operacional documentado:** Todos os PFX de todos os Tenants são criptografados com a mesma chave AES (`CERT__EncryptionKey`). Se essa chave vazar, todos os certificados ficam expostos. Procedimento de rotação: parar emissões → re-criptografar todos os registros com a nova chave → trocar variável de ambiente → retomar emissões. Reavaliar envelope encryption com versionamento de chave se o número de tenants crescer.

### DA-06 — Resposta assíncrona com webhook e polling

**Decisão:** A emissão de NFC-e retorna `202 Accepted` imediatamente. O resultado é entregue via webhook (primário) ou disponibilizado para polling (fallback).

**Justificativa:**
- A comunicação com o SEFAZ pode demorar de 1s a 30s+ dependendo do estado e carga.
- Resposta síncrona travaria o cliente em timeouts.
- O `X-Idempotency-Key` obrigatório garante que retries do cliente não gerem notas duplicadas.

**Consulta de situação antes de retry (obrigatório):**
Antes de qualquer retry após timeout ou falha de conexão com o SEFAZ, o `HangfireJobProcessor` **deve** chamar `nfeConsultaNFe` para verificar se a nota já foi autorizada na tentativa anterior. Sem essa consulta, há risco real de rejeição `cStat=204` (duplicidade — nota já autorizada) ou de emissão duplicada em caso de falha parcial de rede.

**Autorização descoberta via consulta (fluxo distinto):** Quando `nfeConsultaNFe` retorna `cStat=100` após um timeout anterior (autorização já existente, descoberta pela consulta), o fluxo é: chamar `Autorizar(protocolo, xmlConsulta)`, registrar `DeliveryAttempt` com `TipoTentativa=Consulta` e `Success=true`, publicar `DocumentoFiscalAutorizadoEvent`. **Não** retentar o envio. Este fluxo é distinto do "autorizado agora" (envio direto retornou 100).

**`indPres` parametrizável:**
O campo `indPres` não deve ser hardcoded como `1` no XML. O `IssueDocumentCommand` deve aceitar o valor informado pelo caller, com padrão `1` (presencial). O hub valida que apenas os valores permitidos para NFC-e são aceitos (1, 3, 4, 9). O valor `2` (internet) é **proibido** para NFC-e (rejeição 717).

**Segurança do webhookSecret:**
- O hub gera o `webhookSecret` automaticamente na criação do ClienteApp, usando `RandomNumberGenerator` com comprimento mínimo de 32 bytes representados em hex (64 caracteres).
- O valor é retornado **uma única vez** no response do `POST /api/v1/clientes`. Após isso, é irrecuperável — apenas rotacionável via `POST /api/v1/clientes/{id}/rotate-webhook-secret`.
- O `webhookSecret` é armazenado **criptografado com AES-256-GCM** (mesma chave `CERT__EncryptionKey`), análogo ao CSC do Tenant. Nunca armazenado em texto claro.
- O `WebhookDeliveryService` decriptografa o secret na hora do uso para assinar o payload.

**Comportamento durante indisponibilidade do SEFAZ:**
O hub possui retry automático via Hangfire por até ~2 horas acumulados. Se o SEFAZ ficar indisponível por mais de 2 horas: documentos em fila transitam para `Falhou`; novos documentos continuam aceitando `202 Accepted` mas não serão processados até o SEFAZ retornar. O hub **não emite em contingência offline** (tpEmis=9). O ClienteApp deve tratar webhooks com `status=error` como indicativo de falha e implementar fluxo alternativo próprio.

**Contrato do response de polling (`DocumentoStatusResponse`):**
O campo `xmlAssinado` **não é retornado** no `DocumentoStatusResponse`. Se o ClienteApp precisar do XML completo, deve existir endpoint dedicado `GET /api/v1/documentos/{id}/xml` com controle de acesso próprio. O polling retorna apenas: `documentoId`, `status`, `chaveAcesso?`, `qrCode?`, `protocolo?`, `motivoRejeicao?`, `authorizedAt?`.

**Contrato:**

```
POST /nfce
  → 202 Accepted
  → Body: { documentId, status: "processing", pollUrl: "/nfce/{documentId}/status" }

Webhook (quando configurado):
  POST <webhookUrl>
  Body: { documentId, status: "authorized|rejected|error", chaveAcesso, qrCode, protocolo?, motivo? }
  Header: X-Hub-Signature-256: sha256=<hex>
  (NÃO inclui o XML completo)

Polling:
  GET /nfce/{documentId}/status
  → { documentoId, status, chaveAcesso?, qrCode?, protocolo?, motivoRejeicao?, authorizedAt? }
  (NÃO inclui o XML completo)
```

### DA-07 — Roteamento por UF (SefazEndpointResolver multiestado)

**Decisão:** O hub mantém internamente uma tabela de roteamento de webservices SEFAZ por UF e tipo de documento. O resolver usa o conceito de **grupo autorizador** (conforme documentação técnica do SEFAZ), mapeando `ufCodigo → GrupoAutorizador → URL`.

**Grupos autorizadores:**
- **SVRS** — cobre os estados sem infraestrutura própria para NFC-e: AC, AL, AP, CE, DF, ES, MA, PA, PB, PE, PI, RJ, RN, RO, RR, SC, SE, TO (16+ estados).
- **SEFAZ-SP** — São Paulo (maior volume de NFC-e do Brasil, URLs próprias).
- **SEFAZ-MG**, **SEFAZ-RS**, **SEFAZ-PR**, **SEFAZ-BA**, **SEFAZ-MT** — infraestrutura própria.
- **Sandbox AM** — ambiente de desenvolvimento, aceita qualquer certificado.

**Caso específico:** Sergipe (SE, ufCodigo=28) pertence ao grupo SVRS.

| Ambiente | URL base SVRS (NFC-e) |
|---|---|
| Homologação | `https://nfce-homologacao.svrs.rs.gov.br/` |
| Produção | `https://nfce.svrs.rs.gov.br/` |

**Expansão:** Adicionar novas UFs significa adicionar entrada no dicionário `ufCodigo → GrupoAutorizador` — sem alterar a assinatura do resolver.

### DA-08 — Sandbox de desenvolvimento

**Decisão:** O ambiente de desenvolvimento/testes usa o sandbox do Amazonas (AM), que aceita qualquer certificado (inclusive autoassinado ou sem certificado).

**Parâmetros do sandbox AM:**
- CSC: `0123456789`
- cIdToken: `000001`
- Não exige certificado A1 real

**Justificativa:** Permite desenvolver e testar sem precisar de certificado A1 real, reduzindo barreira de entrada no desenvolvimento.

### DA-09 — Preparação para Reforma Tributária 2026 (NT 2025.002)

**Decisão:** A arquitetura de geração de XML deve ser extensível para adicionar os campos IBS (Imposto sobre Bens e Serviços), CBS (Contribuição sobre Bens e Serviços) e IS (Imposto Seletivo) sem redesign.

**Prazo normativo:** Campos obrigatórios a partir de 01/01/2026 conforme NT 2025.002.

**Abordagem:** Separar a construção do XML por versão de schema. Usar strategy/builder pattern para cada bloco fiscal, permitindo adicionar novos campos sem alterar o core do pipeline.

### DA-10 — Observabilidade

| Ferramenta | Uso |
|---|---|
| Serilog | Structured logging com enrichers (tenantId, clientAppId, documentId, correlationId) |
| Scalar UI | Documentação interativa da API (substituição ao Swagger UI) |
| Hangfire Dashboard | Monitoramento de filas, jobs e falhas de envio ao SEFAZ — protegido com autenticação básica via variável de ambiente em produção |
| OpenTelemetry | Tracing distribuído com exporter OTLP; instrumentação de ASP.NET Core, Npgsql e Hangfire via pacote `AddHangfireInstrumentation()` |

**Política de logging vs OpenTelemetry:** `LoggingBehavior` loga apenas erros e warnings (`Result.IsFailure`). O happy path é responsabilidade do tracing OpenTelemetry (spans com duração e contexto). Isso evita duplicação de informação entre logs estruturados e traces.

**Serilog enrichers:** Um middleware de enrichment (ou configuração de `UseSerilogRequestLogging` com `EnrichDiagnosticContext`) deve ler `ICurrentUserContext` para adicionar `TenantId` e `ClienteAppId` a cada log de request. Sem isso, os enrichers não produzem valores.

**Jobs Hangfire — dados sensíveis (obrigatório):** Jobs Hangfire devem receber **apenas IDs** como argumentos (ex: `Guid documentoId`). O payload com dados da nota (chaveAcesso, protocolo, XML) deve ser carregado pelo job a partir do banco no momento da execução — **nunca** serializado como argumento do job. Isso evita exposição de dados fiscais no Hangfire Dashboard e no schema Hangfire no banco.

### DA-11 — Resiliência e State Machine do DocumentoFiscal

**Decisão:** O ciclo de vida do `DocumentoFiscal` é controlado por uma state machine explícita com proteção contra estados inconsistentes.

**Transições válidas:**

```
Criado → Enfileirado → Processando → Autorizado
                       Processando → Rejeitado
                       Processando → Falhou        (timeout de reconciliação)
                       Autorizado  → Cancelado     (dentro de 30 min)
                       Autorizado  → Denegado      (cStat=110 — definitivo)
```

Transições inválidas retornam `Result.Failure(DocumentoFiscalErrors.TransicaoInvalida)`.

**Status `Falhou`:** Estado distinto de `Rejeitado`, adicionado ao enum `StatusDocumento`. O `ReconciliacaoJobProcessor` transiciona documentos de `Processando` para `Falhou` quando confirmado que não foram autorizados pelo SEFAZ após timeout. O `DocumentoFiscalFalhouEvent` é publicado nesta transição.

**Status `Denegado` (cStat=110):** Denegação fiscal é **diferente** de rejeição técnica — significa que o CNPJ do emitente está irregular junto ao SEFAZ (inadimplência, cassação, suspensão). Tratamento diferenciado obrigatório:
- Transicionar para `Denegado` (estado distinto de `Rejeitado`).
- Registrar log de nível `Critical` com `TenantId`, `CNPJ` e `xMotivo`.
- Publicar `DocumentoFiscalDenegadoEvent` para notificação operacional (além do webhook ao ClienteApp).
- O tenant afetado deve ser marcado para revisão manual antes de novas emissões.

**Outbox Pattern — mecanismo completo:**
O `ApplicationDbContext` sobrescreve `SaveChangesAsync` com `DomainEventsInterceptor : SaveChangesInterceptor` (EF Core). Antes do commit:
1. Itera `ChangeTracker.Entries<IDomainEventSource>()`.
2. Serializa cada domain event como `OutboxMessage` com `EventType` (nome qualificado da classe) e `Payload` (JSON via `System.Text.Json`).
3. Insere os `OutboxMessage` na mesma transação atômica do negócio.
4. Limpa a lista `DomainEvents` das entidades após coleta.

O `OutboxRelayJob` (job Hangfire periódico, a cada 30s) lê `outbox_messages WHERE processed_at IS NULL ORDER BY occurred_at LIMIT 50`, usando `SELECT ... FOR UPDATE SKIP LOCKED` para suportar múltiplas instâncias sem processamento duplicado.

**Domain Events obrigatórios:**
- `DocumentoFiscalAutorizadoEvent` — publicado ao autorizar o documento
- `DocumentoFiscalRejeitadoEvent` — publicado ao rejeitar o documento
- `DocumentoFiscalCanceladoEvent` — publicado ao cancelar o documento
- `DocumentoFiscalFalhouEvent` — publicado ao transicionar para `Falhou`
- `DocumentoFiscalDenegadoEvent` — publicado ao receber cStat=110
- `TenantProvisionadoEvent` — publicado ao criar um novo Tenant

**Webhook via Domain Events:**
O dispatch do webhook deve ser acionado por um handler de `DocumentoFiscalAutorizadoEvent`, **não** inline no handler de emissão. Isso garante separação de responsabilidades e permite que o webhook seja reenfileirado independentemente da emissão.

**WebhookDeliveryService:** O `DocumentoFiscalAutorizadoEventHandler` chama `IWebhookDeliveryService.DeliverAsync(documentoId, clienteAppId)`. O `WebhookDeliveryService`:
1. Decriptografa `webhookSecret` (AES-GCM).
2. Valida SSRF antes do dispatch (ver DA-12).
3. Assina payload com HMAC-SHA256.
4. Registra tentativa em `DeliveryAttempt`.
5. Enfileira retry via Hangfire com backoff: 3 tentativas (30s, 5min, 30min).

### DA-12 — Segurança do Webhook

**Decisão:** Webhooks enviados pelo hub devem ser seguros e verificáveis pelo receptor.

**Requisitos:**
- A URL do webhook é validada **no cadastro** do ClienteApp contra uma allowlist de esquemas (apenas `https`) e bloqueada para ranges RFC 1918 (192.168.x.x, 10.x.x.x, 172.16-31.x.x), loopback (127.x.x.x, ::1) e link-local (169.254.x.x) — proteção anti-SSRF.
- **Proteção anti-SSRF em runtime (obrigatório):** `HttpClientHandler.AllowAutoRedirect = false` no cliente HTTP do `WebhookDeliveryService`. Redirecionamentos HTTP podem bypassar a validação feita no cadastro — um `302 Location: http://169.254.169.254/...` em resposta ao webhook entregaria a requisição a endpoints de metadata de instâncias cloud. Se redirecionamentos precisarem ser seguidos, cada destino deve ser revalidado com a mesma lógica anti-SSRF.
- O payload do webhook é assinado com HMAC-SHA256 usando o `webhookSecret` do ClienteApp (armazenado criptografado). O header `X-Hub-Signature-256: sha256=<hex>` é incluído em toda requisição de webhook, permitindo que o receptor verifique autenticidade.
- O Hangfire Dashboard é protegido com autenticação básica via variável de ambiente em produção. Não deve ser exposto publicamente sem autenticação.

---

## 3. Modelo de Identidade

O hub opera com três níveis hierárquicos de identidade:

```
ClienteApp (ex: "visu", "sistemaX")
  └── Tenant (empresa emissora — CNPJ específico)
       └── DocumentoFiscal (NFC-e, NF-e, NFS-e)
```

### ClienteApp

Representa um sistema de software que integra com o hub.

| Campo | Descrição |
|---|---|
| `id` | UUID gerado pelo hub |
| `name` | Nome identificador (ex: "visu") |
| `client_id` | Identificador público para OAuth2 |
| `client_secret_hash` | Segredo derivado via PBKDF2 com SHA-256, mínimo 600.000 iterações (OWASP 2024+) |
| `webhookUrl` | URL para recebimento de notificações (opcional; apenas `https`, bloqueado para RFC 1918) |
| `webhookSecret` (criptografado) | HMAC secret para assinatura dos payloads de webhook — gerado pelo hub, retornado apenas uma vez, armazenado criptografado com AES-256-GCM |
| `isActive` | Flag de ativação |

**Ciclo de vida do client_secret:**
- O valor bruto do `client_secret` é retornado **uma única vez** no response do `POST /api/v1/clientes`.
- Para rotação de credenciais comprometidas: `POST /api/v1/clientes/{id}/rotate-secret` (protegido por `X-Admin-Key`). Gera novo secret, retorna o valor uma única vez, invalida o anterior imediatamente. Tokens JWT emitidos com o secret antigo expiram naturalmente (sem blacklist).

**Regras:**
- Um ClienteApp pode ter múltiplos Tenants.
- O ClienteApp autentica no hub via Client Credentials e opera sobre seus próprios Tenants.
- Não pode acessar Tenants de outros ClienteApps.

### Tenant

Representa uma empresa emissora de notas fiscais.

| Campo | Descrição |
|---|---|
| `id` | UUID gerado pelo hub (este é o valor do `X-Tenant-Id` nos headers) |
| `clientAppId` | FK para o ClienteApp dono |
| `cnpj` | CNPJ da empresa (14 dígitos, sem máscara) |
| `razaoSocial` | Razão social |
| `nomeFantasia` | Nome fantasia (opcional) |
| `endereco` | Endereço completo do emitente (value object `Endereco`, mapeado via `OwnsOne`) |
| `configuracaoFiscal` | Value object contendo: `Crt`, `Serie`, `Ambiente`, `UfCodigo` (mapeado via `OwnsOne`) |
| `csc` (criptografado) | Código de Segurança do Contribuinte para QR Code — armazenado criptografado com AES-256-GCM (`bytea`) |
| `cIdToken` | Identificador do CSC (6 dígitos com zeros à esquerda) |
| `certificadoPfxCriptografado` | Bytes do PFX criptografados com AES-256-GCM (`bytea`) |
| `certificadoSenhaCriptografada` | Senha do PFX criptografada (`bytea`) |
| `certificadoVencimento` | DateTime de vencimento do certificado A1 (para monitoramento) |
| `isActive` | Flag de ativação |

**Nota:** `ConfiguracaoFiscal` é um value object do Tenant mapeado via EF Core `OwnsOne`, com colunas físicas `crt`, `serie`, `ambiente`, `uf_codigo`. O `Csc` e `CIdToken` são campos diretos no Tenant (não no value object) pois envolvem criptografia no nível de infraestrutura — manter fora do value object preserva a semântica de imutabilidade.

**Regras:**
- Um Tenant pertence a exatamente um ClienteApp.
- O hub valida o vínculo ClienteApp-Tenant em toda operação (não é possível emitir nota de Tenant de outro ClienteApp).
- Auto-provisioning via API: o ClienteApp cria e gerencia seus Tenants programaticamente.
- O `X-Tenant-Id` nos headers contém o UUID do Tenant, nunca o CNPJ.

### DocumentoFiscal

Representa uma nota fiscal emitida ou em processo de emissão.

| Campo | Descrição |
|---|---|
| `id` | UUID gerado pelo hub (retornado no 202) |
| `tenantId` | FK para o Tenant emitente |
| `idempotencyKey` | Chave fornecida pelo cliente (única por Tenant) |
| `tipo` | "NFC-e", "NF-e", "NFS-e" |
| `chaveAcesso` | 44 dígitos (gerada pelo hub) |
| `numero` | Número sequencial da nota (gerado pelo hub) |
| `serie` | Série utilizada |
| `status` | `Criado`, `Enfileirado`, `Processando`, `Autorizado`, `Rejeitado`, `Cancelado`, `Falhou`, `Denegado` |
| `xmlAssinado` | XML completo assinado (armazenado após emissão — contém CPF do consumidor quando informado) |
| `protocolo` | nProt retornado pelo SEFAZ |
| `qrCodeUrl` | URL do QR Code para DANFE |
| `motivoRejeicao` | Mensagem de rejeição (quando aplicável) |
| `createdAt` | Data/hora de criação |
| `authorizedAt` | Data/hora de autorização pelo SEFAZ |

---

## 4. Requisitos de Negócio

### Prioridade de implementação

| Fase | Documento | Modelo | Status |
|---|---|---|---|
| Fase 1 | NFC-e | Modelo 65 | Implementar primeiro |
| Fase 2 | NF-e | Modelo 55 | Segunda iteração |
| Fase 3 | NFS-e | — | Terceira iteração (fora do escopo fiscal federal) |

### RN-01 — Transparência fiscal para o cliente

O ClienteApp envia apenas dados de negócio. O hub é responsável por toda a lógica fiscal.

| Enviado pelo ClienteApp | Calculado/Gerado pelo Hub |
|---|---|
| Produtos (nome, quantidade, valor unitário, NCM, CEST) | ICMS, PIS, COFINS (com CST/CSOSN corretos por CRT) |
| Forma de pagamento | Chave de acesso (44 dígitos) |
| Dados do consumidor (opcional) | cNF (8 dígitos — gerado com `RandomNumberGenerator`, nunca `Random.Shared`) |
| Série desejada (opcional) | cDV (dígito verificador por Módulo 11) |
| `indPresenca` (opcional; padrão 1) | Número sequencial da nota |
| — | QR Code (URL + SHA1 com CSC) |
| — | Assinatura digital RSA-SHA1 |
| — | Montagem do XML NFC-e conforme schema SEFAZ |
| — | `vTotTrib` calculado via tabela IBPT por NCM |

### RN-02 — Regime tributário por tenant

| CRT | Regime | ICMS | PIS | COFINS | CSOSN Aplicável |
|---|---|---|---|---|---|
| 1 | Simples Nacional | CSOSN (ex: 400, 102, 500) | CST 07 (PISNT) | CST 07 (COFINSNT) | 102, 300, 400, 500, 900 |
| 2 | Simples Nacional — Excesso de sublimite de receita bruta | CSOSN 900 com destaque de ICMS (base de cálculo e alíquota normais) | CST normal | CST normal | 900 |
| 3 | Regime Normal (Lucro Real / Presumido) | CST normal (ex: 00, 20, 40, 60) | CST normal (ex: 01, 02, 03) | CST normal | — |

O ClienteApp informa o CRT ao cadastrar o Tenant. O hub aplica automaticamente as regras corretas — o ClienteApp não precisa conhecer tributação.

**CRT 2 — detalhe:** Usa `<ICMSSN900>` no XML com campos de base de cálculo preenchidos (diferente do CRT 1 que usa CSOSN sem destaque de valor). PIS/COFINS com CST normal (ex: CST 01 com base de cálculo e alíquota).

### RN-03 — Identificação do consumidor em NFC-e

| Situação | Limite |
|---|---|
| Sem identificação do consumidor | Até R$ 10.000,00 (limite nacional; estados podem ter limite menor) |
| Com CPF do consumidor | Até R$ 200.000,00 |
| Com CNPJ no destinatário | **Proibido** para NFC-e (vigente desde novembro de 2025) |

O hub deve suportar emissão com e sem identificação do consumidor, validando o limite de valor conforme a situação. O CPF do consumidor é armazenado **apenas no `xmlAssinado`** (XML fiscal obrigatório) — não deve ser armazenado em coluna separada em texto claro.

**CNPJ/CPF — normalização de entrada:** O hub aceita CNPJ/CPF com ou sem máscara de formatação (`11.222.333/0001-81` ou `11222333000181`). A normalização (remoção de `.`, `/`, `-`) é aplicada automaticamente antes da validação dos dígitos verificadores. O value object `Cnpj`/`Cpf` armazena sempre sem máscara (dígitos puros).

### RN-04 — Auto-provisioning de Tenants

- O ClienteApp pode criar, atualizar e desativar Tenants via API sem intervenção manual no hub.
- O hub valida o CNPJ (dígitos verificadores) no cadastro.
- Certificado A1 enviado pelo ClienteApp no cadastro do Tenant (upload PFX + senha).
- O endpoint `POST /api/v1/clientes` é protegido por `X-Admin-Key` — chave administrativa gerada na instalação (obrigatório, não opcional).
- Upload de certificado PFX limitado a 50KB (`RequestSizeLimit`). PFX legítimos têm tipicamente 2–4KB; o limite previne upload de arquivos maliciosos de grande volume.

### RN-05 — Sequencialidade de numeração

- O hub controla o número sequencial das notas por Tenant + série usando `SEQUENCE` do PostgreSQL por tenant+série.
- A sequence é criada **no `CreateTenantCommandHandler`**, na mesma operação de provisionamento do Tenant, via `ExecuteSqlRawAsync($"CREATE SEQUENCE IF NOT EXISTS seq_nfe_{tenantIdHex}_{serieSanitizada} START 1 INCREMENT 1")`.
- O nome da sequence usa `tenantId.ToString("N")` (UUID sem hífens, 32 caracteres hex) para gerar identificador PostgreSQL válido. A `serie` é validada com regex `^[0-9]{1,3}$` antes da interpolação — única prevenção contra SQL injection no DDL.
- **"Sem gaps" significa ausência de saltos visíveis na sequência de notas autorizadas**, não ausência absoluta de buracos no banco. Sequências PostgreSQL são non-transactional por design — rollbacks causam buracos no banco, mas o SEFAZ aceita isso (não há rejeição por gap em si).
- O que o SEFAZ rejeita é o **reuso de número já autorizado** (rejeição 509). O hub nunca reusa números: uma vez consumido da sequence, o número é permanente para aquele documento, mesmo que o documento seja rejeitado ou cancelado.
- Gaps por rollback são esperados, documentados e aceitos pelo SEFAZ.

### RN-06 — Cancelamento de NFC-e

- NFC-e pode ser cancelada em até 30 minutos após a autorização (regra geral; estados podem ter prazo menor).
- Após cancelamento, o hub envia o evento de cancelamento ao SEFAZ e atualiza o status do documento.
- O cancelamento usa o webservice `NfeRecepcaoEvento4` (diferente do `NFeAutorizacao4` da emissão). O XML de cancelamento tem estrutura distinta: elemento `<evento>`, `<infEvento>`, `tpEvento=110111`, `detEvento.descEvento="Cancelamento"`, `nProt` obrigatório (número do protocolo de autorização), `xJust` (justificativa de 15–255 chars).

> **Nota de implementação:** O fluxo de cancelamento será implementado na Fase 6B (após a Fase 6 de emissão estar estabilizada). O endpoint `POST /api/v1/documentos/{id}/cancelar` não faz parte do MVP, mas é requisito de negócio documentado e deve ser previsto na arquitetura.

---

## 5. Regras de Negócio

### 5.1 Campos fixos da NFC-e (Modelo 65)

Estes campos são fixos para toda NFC-e emitida pelo hub — o ClienteApp não pode alterá-los:

| Campo XML | Valor | Significado |
|---|---|---|
| `mod` | 65 | Modelo NFC-e |
| `tpNF` | 1 | Nota de saída |
| `idDest` | 1 | Operação interna (dentro do estado) |
| `tpImp` | 4 | DANFE NFC-e |
| `finNFe` | 1 | NF-e normal |
| `indFinal` | 1 | Consumidor final |
| `indPres` | 1 (padrão) | Presencial — parametrizável via `indPresenca` no request (valores válidos: 1, 3, 4, 9) |
| `procEmi` | 3 | Emissão por aplicativo do contribuinte via API — obrigatório no schema |
| `verProc` | `VisuFiscalHub 1.0` | Versão do software emissor — obrigatório no schema |

**`dhEmi` (data/hora de emissão):**
Deve ser serializado com o offset do fuso horário da UF do Tenant — nunca UTC puro ou sem offset. O hub mantém internamente o mapa completo de todas as 27 UFs como dicionário estático `static readonly Dictionary<int, TimeSpan>` com offsets fixos (sem horário de verão, conforme decreto federal de suspensão).

Tabela canônica UF → offset UTC:

| UFs | Offset | cUF exemplos |
|---|---|---|
| AM, MT, MS, RO, RR | -04:00 | 13, 51, 50, 11, 14 |
| SE, SP, MG, RJ, BA, RS, SC, PR, ES, GO, TO, MA, PA, AP, PI, CE, RN, PB, PE, AL, RR, DF | -03:00 | 28, 35, 31, 33, 29, 43, 42, 41, 32, 52, 17, 21, 15, 16, 22, 23, 24, 25, 26, 27, 14, 53 |
| AC | -05:00 | 12 |
| Fernando de Noronha (PE) | -02:00 | — (subconjunto de PE) |

Para NFC-e de varejo, o fuso de Sergipe (SE, ufCodigo=28) é `-03:00`. Exemplos:
- Sergipe (SE, UTC-3): `2026-05-11T14:30:00-03:00`
- Amazonas (AM, UTC-4): `2026-05-11T14:30:00-04:00`
- Acre (AC, UTC-5): `2026-05-11T14:30:00-05:00`

**`vTotTrib`:**
Obrigatório pela Lei 12.741/2012. Calculado via tabela IBPT por NCM. A tabela IBPT é atualizada semestralmente e deve haver um serviço interno `IBPTService` com a tabela local na versão mais recente disponível.

**IBPTService — estratégia de atualização:** A tabela IBPT é armazenada como **embedded resource** no assembly `VisuFiscalHub.Infrastructure` (CSV compilado junto com o binário). Atualização semestral requer nova versão da imagem Docker — aceitável para frequência de 2x/ano. O serviço loga a versão e data de referência da tabela no startup. Adicionar `IBPT__TabelaVersao` e `IBPT__DataReferencia` em `appsettings.json` e um health check `IBPTHealthCheck` que emite alerta se a tabela tiver mais de 7 meses.

**Comportamento de borda do IBPTService:** NCM inválido (menos de 8 dígitos, não numérico) ou ausente na tabela IBPT retorna alíquota 0,00 e emite `Log.Warning` — **não** lança exceção (para não bloquear emissão por dado de tabela). O `vTotTrib = 0` é tecnicamente aceito pelo SEFAZ mas deve ser monitorado.

### 5.2 Chave de acesso (44 dígitos)

Estrutura e responsabilidade de geração de cada campo:

```
[cUF(2)] [AAMM(4)] [CNPJ(14)] [mod(2)] [serie(3)] [nNF(9)] [tpEmis(1)] [cNF(8)] [cDV(1)]
```

| Campo | Tamanho | Origem |
|---|---|---|
| `cUF` | 2 | Código IBGE da UF do emitente (ex: 28 = SE) |
| `AAMM` | 4 | Ano e mês de emissão (ex: 2605 para mai/2026) |
| `CNPJ` | 14 | CNPJ do emitente (sem pontuação) |
| `mod` | 2 | Modelo do documento: 65 (NFC-e) |
| `serie` | 3 | Série da nota (zero-padded à esquerda) |
| `nNF` | 9 | Número sequencial da nota (zero-padded à esquerda) |
| `tpEmis` | 1 | Tipo de emissão: 1 (normal) ou 9 (contingência) |
| `cNF` | 8 | **Gerado pelo hub:** 8 dígitos aleatórios via `RandomNumberGenerator.GetBytes(4)` convertido para 8 dígitos decimais — **nunca** `Random.Shared` |
| `cDV` | 1 | **Calculado pelo hub:** módulo 11 sobre os 43 primeiros dígitos |

**Algoritmo cDV (Módulo 11):**
1. Multiplica cada dígito dos 43 primeiros da chave por um peso cíclico de 2 a 9 (da direita para a esquerda).
2. Soma os produtos.
3. `resto = soma % 11`
4. Se `resto < 2` → `cDV = 0`; senão → `cDV = 11 - resto`

**Valor de teste concreto (MOC 7.0, Seção 4.1.6):**
- 43 dígitos: `3509061420016714006512500100000180010000009`
- cDV esperado: `7`
- Chave completa: `35090614200167140065125001000001800100000097`

### 5.3 QR Code (obrigatório para NFC-e)

O QR Code é composto por uma URL com parâmetros da nota e um hash de segurança:

```
URL final: {urlConsultaSEFAZ}?p={chaveAcesso}|2|{tpAmb}|{cHashQRCode}
```

**Cálculo do hash (fórmula correta conforme NT 2019.001 v1.50):**

```
cHashQRCode = SHA1( chaveAcesso + "|2|" + tpAmb + "|" + CSC )
```

O `|2|` é a **versão do QR Code** (fixo, valor literal "2") — não confundir com `tpAmb`. A sequência completa de campos na string de entrada para SHA-1 é: `chaveAcesso`, `|2|` (versão), `tpAmb` (1 ou 2), `|`, `CSC`.

**Valor de teste concreto para validação (sandbox AM):**
- `chaveAcesso = "35090614200167140065125001000001800100000097"` (chave MOC 7.0)
- `tpAmb = 2` (homologação)
- `CSC = "0123456789"` (sandbox AM)
- String para SHA-1: `35090614200167140065125001000001800100000097|2|2|0123456789`
- Hash SHA-1 hex: calcular com `echo -n "35090614200167140065125001000001800100000097|2|2|0123456789" | sha1sum` e hardcodar no `[InlineData]` dos testes **antes** de implementar o código.

**Regras críticas:**
- O CSC é concatenado apenas para calcular o hash — **nunca aparece na URL final**.
- A `urlConsultaSEFAZ` varia por estado. O hub mantém mapeamento interno por UF (via `SefazEndpointResolver.ResolverUrlConsultaQr`).
- Para homologação, usar a URL de homologação do estado (ou SVRS para SE).

**Campos obrigatórios no XML (`infNFeSupl`):**
```xml
<infNFeSupl>
  <qrCode>{URL completa com hash}</qrCode>
  <urlFe>{URL base de consulta}</urlFe>
</infNFeSupl>
```

**Campo obrigatório no `<ide>`:**
```xml
<cIdToken>{identificador do CSC — 6 dígitos com zeros à esquerda}</cIdToken>
```

### 5.4 Tributação por CRT

#### CRT 1 — Simples Nacional

```xml
<ICMS>
  <ICMSSN400>  <!-- ou ICMSSN102, ICMSSN500, conforme operação -->
    <orig>0</orig>
    <CSOSN>400</CSOSN>
  </ICMSSN400>
</ICMS>
<PIS>
  <PISNT>
    <CST>07</CST>
  </PISNT>
</PIS>
<COFINS>
  <COFINSNT>
    <CST>07</CST>
  </COFINSNT>
</COFINS>
```

#### CRT 2 — Simples Nacional Excesso de sublimite

```xml
<ICMS>
  <ICMSSN900>
    <orig>0</orig>
    <CSOSN>900</CSOSN>
    <modBC>3</modBC>
    <vBC>{baseCalculo}</vBC>
    <pICMS>{aliquota}</pICMS>
    <vICMS>{valor}</vICMS>
  </ICMSSN900>
</ICMS>
<PIS>
  <PISAliq>
    <CST>01</CST>  <!-- ou CST normal conforme operação -->
    <vBC>{baseCalculo}</vBC>
    <pPIS>{aliquota}</pPIS>
    <vPIS>{valor}</vPIS>
  </PISAliq>
</PIS>
<!-- COFINS análogo ao PIS -->
```

#### CRT 3 — Regime Normal

O hub calcula base de cálculo, alíquota e valor dos tributos conforme os parâmetros do produto e do tenant.

### 5.5 Totais e validação (evitar rejeição 610)

O SEFAZ valida que `vNF` (valor total da nota) corresponde ao somatório dos itens. Tolerância: R$ 0,50.

O hub deve:
1. Calcular todos os totais internamente antes de montar o XML.
2. Garantir que `vNF = vProd - vDesc + vFrete + vSeg + vOutro + vII + vIPI + vIPIDevol + vPIS + vCOFINS + vICMSST` (conforme regra do schema).
3. Rejeitar a requisição com erro descritivo antes de enviar ao SEFAZ se os valores não fecharem.

### 5.6 Rejeições críticas do SEFAZ — interpretação correta de cStat

O SEFAZ usa códigos `cStat` que **não seguem a semântica HTTP**. A classificação correta:

| cStat | Interpretação | Ação |
|---|---|---|
| `100` | **Autorizado** — único código 1xx que representa sucesso | Finalizar como Autorizado |
| `101` | Cancelado (definitivo) | Não retentar |
| `110` | **Denegado** (definitivo) — CNPJ irregular junto ao SEFAZ | Transicionar para `Denegado`; alertar operacional; **nunca** tratar como rejeição comum |
| `204` | **Duplicidade já autorizada** — rejeição definitiva | Não retentar; buscar nota original pelo número |
| Outros `2xx` | Mistos — podem ser temporários | Avaliar caso a caso |
| `3xx` | Erros de serviço temporários | Retentar com backoff |
| `4xx` (exceto 110, 204) | Rejeições definitivas — erro no documento | Não retentar; corrigir e reemitir |
| `5xx` | Erros de infraestrutura temporários | Retentar com backoff |
| Timeout / ConnectionRefused | Falha de comunicação | **Consultar `nfeConsultaNFe` antes de retentar** |

**Rejeições críticas a evitar no hub:**

| Código | Descrição | Prevenção |
|---|---|---|
| 509 | Número de NF-e já utilizado | Nunca reutilizar números da sequence |
| 610 | vNF não bate com somatório dos itens (tolerância R$ 0,50) | Calcular e validar totais antes de montar o XML |
| 709 | tpImp diferente de 4 | Hardcoded: `tpImp=4` para toda NFC-e |
| 714 | tpEmis inválido (NFC-e aceita apenas 1 ou 9) | Validar no pipeline antes de enviar |
| 715 | finNFe diferente de 1 | Hardcoded: `finNFe=1` para toda NFC-e |
| 717 | indPres=2 (proibido para NFC-e) | Validar no `IssueDocumentCommandValidator`: rejeitar `indPres=2` |

### 5.7 Reforma Tributária — NT 2025.002 (IBS/CBS/IS)

A partir de 01/01/2026, os campos de IBS, CBS e IS passam a ser obrigatórios nos documentos fiscais eletrônicos.

**Preparação arquitetural:**
- O builder de XML deve ter uma extensão dedicada para os novos grupos tributários.
- Os campos podem estar presentes com valores zerados até a entrada em vigor completa.
- Monitorar publicação dos schemas atualizados pelo SEFAZ para incorporar ao hub.

---

## 6. Requisitos Não-Funcionais

### RNF-01 — Resiliência na comunicação com o SEFAZ

| Situação | Comportamento |
|---|---|
| SEFAZ indisponível | Hangfire retry automático: 30s → 120s → 600s → 1800s → 3600s (~2h acumulados) |
| Timeout na resposta | Chamar `nfeConsultaNFe` para verificar status; só retentar envio se confirmado que não foi processado |
| Rejeição definitiva (4xx cStat, 110, 204) | Não retentar; gravar motivo e notificar via webhook |
| Duplicata (`X-Idempotency-Key`) | Retornar resposta anterior sem reprocessar |
| Documento em `Processando` por mais de 10 min | `ReconciliacaoJobProcessor` transiciona para `Falhou` após consulta `nfeConsultaNFe` |
| SEFAZ indisponível por mais de 2h | Documentos transitam para `Falhou`; hub não emite em contingência offline |

### RNF-02 — Idempotência

- O header `X-Idempotency-Key` é **obrigatório** em todo POST de emissão.
- O hub armazena a chave por Tenant e rejeita com `409 Conflict` se a chave já foi processada com status final.
- Se status ainda for `Processando`, retorna `200` com o documento existente.
- Chaves são mantidas por tempo configurável (padrão: 24 horas).

### RNF-03 — Segurança

| Aspecto | Implementação |
|---|---|
| Autenticação da API | JWT Bearer RS256 (Client Credentials) |
| Isolamento de dados | Toda query filtra por `clientAppId` extraído do JWT |
| Identificação de tenant em headers | UUID interno do Tenant (`X-Tenant-Id`) — nunca CNPJ |
| Certificados A1 | Criptografados com AES-256-GCM; nonce gerado com `RandomNumberGenerator.GetBytes(12)` a cada encrypt |
| CSC do tenant | Criptografado com AES-256-GCM (mesma chave `CERT__EncryptionKey`) |
| webhookSecret | Criptografado com AES-256-GCM — gerado pelo hub, retornado uma única vez na criação |
| Chave de criptografia dos certificados | Armazenada em variável de ambiente (nunca no banco) |
| HTTPS | Obrigatório em produção; mTLS para comunicação com SEFAZ |
| `cNF` da chave de acesso | Gerado com `RandomNumberGenerator` (criptograficamente seguro) — nunca `Random.Shared` |
| CPF do consumidor | Armazenado apenas no `xmlAssinado` (XML fiscal obrigatório); não armazenar em coluna separada em texto claro |
| `ClientSecretHash` | PBKDF2 com SHA-256, mínimo 600.000 iterações (OWASP 2024+) |
| Rotação de client_secret | `POST /api/v1/clientes/{id}/rotate-secret` (protegido por `X-Admin-Key`) |
| Rate limiting `/auth/token` | Máximo 10 requisições por minuto por IP (via `AddRateLimiter`) |
| Rate limiting endpoints de emissão | 100 req/min por `client_id` — partição via `httpContext.User.FindFirst("client_id")?.Value` |
| Rate limiting `POST /api/v1/clientes` | 5 req/min por IP (independente da `X-Admin-Key`) |
| Endpoint `POST /api/v1/clientes` | Protegido por `X-Admin-Key`; comparação time-safe via `CryptographicOperations.FixedTimeEquals` |
| Upload de certificado PFX | Limitado a 50KB via `RequestSizeLimit` |
| Comportamento do 429 | Retorna `429 Too Many Requests` com header `Retry-After: {segundos}` |
| Webhook | URL validada no cadastro (apenas `https`, sem RFC 1918); `AllowAutoRedirect=false` em runtime; payload assinado com HMAC-SHA256 |
| Jobs Hangfire | Recebem apenas IDs como argumentos — dados sensíveis nunca serializados como argumento |
| Hangfire Dashboard | Autenticação básica obrigatória em produção |
| Monitoramento de certificados | Alertar certificados com vencimento em menos de 30 dias |
| Logs | Nunca logar dados sensíveis (senha do certificado, CSC completo, CPF completo) |

### RNF-04 — Performance

- Tempo de resposta da API (202 Accepted): < 200ms p99
- Throughput alvo: suportar múltiplos Tenants emitindo simultaneamente
- Cache de certificado por Tenant para evitar decriptografia repetida (cache armazena `byte[]`, não `X509Certificate2`)
- Conexões ao banco via pool gerenciado pelo EF Core / Npgsql
- `IHttpClientFactory` para `SefazHttpClient` — evita socket exhaustion

### RNF-05 — Observabilidade

| Recurso | Ferramenta |
|---|---|
| Structured logging | Serilog com enrichers de contexto (tenantId, documentId, correlationId) |
| Documentação de API | Scalar UI |
| Monitoramento de jobs | Hangfire Dashboard (protegido em produção) |
| Correlação de requisições | Correlation ID propagado via header e logs |
| Tracing distribuído | OpenTelemetry com exporter OTLP |
| Versão da tabela IBPT | Logada no startup; health check se > 7 meses |

### RNF-06 — Configurabilidade por ambiente

| Configuração | Origem |
|---|---|
| Strings de conexão | Variável de ambiente / secrets |
| Chave AES dos certificados | Variável de ambiente `CERT__EncryptionKey` (nunca hardcoded) |
| JWT private key (PEM) | Variável de ambiente `JWT__PrivateKeyPem` |
| JWT public key(s) (PEM) | Variáveis de ambiente `JWT__PublicKeyPems__0`, `JWT__PublicKeyPems__1` (lista para rotação) |
| Admin Key | Variável de ambiente `AdminKey__Value` |
| Ambiente SEFAZ (homologação/produção) | Por Tenant (banco de dados) |
| URLs dos webservices SEFAZ | Tabela interna do hub (não configurável por tenant) |
| Versão da tabela IBPT | `IBPT__TabelaVersao` em `appsettings.json` |

### RNF-07 — Auditoria

- Todo evento relevante deve ser logado com contexto suficiente para rastreabilidade:
  - Recebimento de requisição de emissão
  - Geração da chave de acesso e número sequencial
  - Envio ao SEFAZ (request/response resumido — sem dados sensíveis)
  - Resultado da autorização ou rejeição
  - Consulta de status (`nfeConsultaNFe`) antes de retry
  - Entrega do webhook (sucesso ou falha)
  - Uso do `AdminKey` (log de auditoria para cada chamada)
  - Denegação fiscal (nível `Critical` com TenantId e CNPJ)

---

## 7. Fora de Escopo

O hub deliberadamente **não** implementa os seguintes itens na fase atual:

| Item | Justificativa |
|---|---|
| SPED Fiscal / EFD-ICMS | Obrigação de escrituração contábil; fora do escopo de emissão |
| NFS-e (Fase 1 e 2) | Padrão municipal, não federal; será abordado na Fase 3 |
| NF-e (Fase 1) | Será implementado na Fase 2, após NFC-e estabilizada |
| Contingência offline (tpEmis=9) | Previsto para implementação futura; não é bloqueador inicial. **Consequência:** durante indisponibilidade SEFAZ > 2h, documentos transitam para `Falhou`. O ClienteApp deve implementar fluxo alternativo próprio. |
| Inutilização de numeração | Operação administrativa; gaps por `Falhou` são aceitos pelo SEFAZ. O `ReconciliacaoJobProcessor` não produz "candidatos a inutilização" — o número simplesmente fica com gap, o que é aceito. |
| Carta de Correção Eletrônica (CC-e) | Operação pós-emissão; prevista para versão futura |
| DANFE em PDF | Geração de PDF do DANFE; responsabilidade do ClienteApp com as informações fornecidas pelo hub |
| Gestão de usuários finais (pessoas físicas) | O hub não gerencia usuários, apenas ClienteApps e Tenants |
| Multiusuário dentro do ClienteApp | Controle de usuários internos do ClienteApp é responsabilidade do próprio ClienteApp |
| Relatórios fiscais e dashboard | O hub expõe dados via API; dashboards são responsabilidade do ClienteApp |
| Integração com SaaS fiscal | Decisão deliberada: o hub é a alternativa direta ao SEFAZ |
| Certificado A3 (token/smartcard) | Suporte apenas a A1 (PFX em software) |
| Cancelamento (MVP) | Previsto na Fase 6B com artefatos: `CancelDocumentCommand`, `CancelDocumentCommandHandler` (valida 30min, status Autorizado), `NfceCancelamentoXmlBuilder`, extensão do `SefazEndpointResolver` para `NfeRecepcaoEvento4`. |
| Anonimização de CPF após 5 anos (LGPD) | Conflito de obrigações: obrigação fiscal de retenção por 5 anos (EFD-ICMS) vs. direito de esquecimento LGPD. Decisão: reter por obrigação fiscal sem anonimização no MVP. Reavaliar com assessoria jurídica. |
| Row-Level Security (PostgreSQL) | Isolamento garantido por filtros na camada de aplicação. Ver DA-01. |

---

## 8. Decisões de Qualidade e Testabilidade

### DA-T01 — Separação de projetos de teste

Dois projetos de teste distintos:

| Projeto | Objetivo |
|---|---|
| `VisuFiscalHub.UnitTests` | Testes de unidade de Domain, Application e componentes de Infrastructure isolados com mocks |
| `VisuFiscalHub.IntegrationTests` | Testes de integração ponta a ponta com banco real e API real |

### DA-T02 — Testes de integração com containers

- `VisuFiscalHub.IntegrationTests` usa `WebApplicationFactory<Program>` combinado com `Testcontainers.PostgreSql`.
- `Program.cs` deve expor `public partial class Program { }` no final do arquivo para compatibilidade com `WebApplicationFactory<Program>`.
- A `CustomWebApplicationFactory` substitui a connection string após o container subir via `ConfigureWebHost`, e aplica migrations via `dbContext.Database.MigrateAsync()` no `IAsyncLifetime.InitializeAsync`.
- `ISefazClient` deve ser substituído por `FakeSefazClient` nos testes de integração — sem conectividade real com o SEFAZ.
- Cada classe de teste integração cria e destrói seu banco em container isolado.
- EF Core InMemory está **proibido** para testes de repositório — não valida constraints, índices únicos ou tipos de coluna PostgreSQL.

### DA-T03 — Metas de cobertura

| Camada | Meta |
|---|---|
| Domain | 95% |
| Application | 90% |
| Infrastructure/Fiscal (XmlBuilder, XmlSigner, TributacaoCalculator) | 85% |
| Infrastructure/Sefaz | **80%** (parsers de `cStat` e resposta SOAP são testáveis com strings hardcoded — sem rede) |
| Api | 80% (coberto pelos testes de integração) |

**Meta Infrastructure/Sefaz revisada para 80%:** Os parsers de `cStat`, `xMotivo` e resposta SOAP malformada são testáveis unitariamente com `string` de resposta hardcoded. Só os testes de conectividade real dependem de rede (smoke tests). A meta de 60% anterior era inadequada.

**Enforcement das metas:** As metas devem ser enforced via `.runsettings` com `<Threshold>` configurado no `coverlet.collector`. Build falha se metas não forem atingidas.

### DA-T04 — Smoke tests com skip condicional

Testes que dependem de conectividade real com o SEFAZ usam `[SkippableFact]` com skip automático quando a variável de ambiente `SEFAZ_SANDBOX_CERT` não está configurada. Esses testes **não bloqueiam o CI padrão**.

```csharp
[SkippableFact]
public async Task EmitirNfce_SandboxAmazonas_DeveRetornarCstat100()
{
    Skip.If(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SEFAZ_SANDBOX_CERT")));
    // ...
}
```

### DA-T05 — TimeProvider injetado

`TimeProvider` deve ser injetado via `IServiceProvider` em todos os handlers e serviços que usam data/hora (`DateTime.UtcNow`, `DateTimeOffset.Now`). Isso permite que testes unitários controlem o tempo sem dependência de relógio do sistema.

**Boundary de cancelamento — teste obrigatório:** O boundary exato de 30 minutos deve ter caso de teste explícito: `Cancelar_QuandoExatamente30Minutos_DeveRetornarErro()`. A regra é `authorizedAt + 30min < utcNow` (menor estrito) — um documento autorizado exatamente 30 minutos atrás **não pode** mais ser cancelado.

---

*Documento mantido pelo time VisuFiscalHub. Atualizar sempre que uma decisão arquitetural for revisada ou um novo requisito for formalmente incorporado.*
