# Design — nota-fiscal-hub como produto plugável de emissão fiscal

**Data:** 2026-07-01
**Status:** Aprovado pelo dono do produto (seções 1, 2 e 3)
**Fontes:** Épico Jira VISX-1188 + 13 histórias filhas; entrevista de refinamento; revisão por time de 5 agentes especializados (arquitetura .NET, tech lead, domínio fiscal, evolução para microsserviços, segurança/multi-tenancy).

---

## 1. Visão do produto

O **nota-fiscal-hub** é um produto SaaS independente e plugável de emissão de notas fiscais brasileiras (NFC-e, NFe, NFSe). Qualquer sistema pluga nele via API: uma farmácia com sistema próprio e 1 CNPJ, uma rede de oficinas desenvolvendo sistema próprio com N empresas, ou o VISU (sistema de barbearias/salões — primeiro cliente, da mesma casa). O hub abstrai SEFAZ e prefeituras: o integrador fala uma API só.

### 1.1 Decisões de produto (fixadas em entrevista)

1. **Motor emissor próprio** — o hub gera o XML, assina com certificado digital A1 e transmite direto à SEFAZ (começando por Sergipe/SEF-SE) e às prefeituras (NFSe via padrão ABRASF com adaptadores). Não é wrapper de Focus/Tecnospeed.
2. **Roadmap:** NFC-e Sergipe → NFSe ABRASF → NFe → DF-e e importação de XML.
3. **Modelo de conta:** multi-tenant API-first; uma conta de integração tem 1..N empresas emissoras (cada uma com CNPJ, certificado A1, séries próprias). O caso 1 conta = 1 empresa não pode ter atrito.
4. **Módulo opcional de cadastros fiscais:** quem já tem NCM/CFOP/CST manda tudo na requisição; quem não tem poderá cadastrar produtos/serviços e classes de imposto no hub e emitir por referência (fase futura).
5. **Interface:** API REST para integradores + Portal web do emissor.
6. **Modelo de emissão híbrido:** NFC-e/NFe síncrona (PDV espera segundos, com contingência); NFSe assíncrona. Webhooks + consulta em ambos.
7. **Planos desde o dia 1 + metering completo;** cobrança automática via gateway fica para módulo futuro. Enforcement de limite é soft no MVP (ver §2.4).
8. **Premissas do épico:** modularizar para venda apartada; raiz duplicável por estado; habilitação via backoffice; XML/PDF na AWS (S3); somente certificado A1.
9. **Arquitetura:** monólito modular, 2 deployables (API + Worker), fronteiras rígidas para extração futura de microsserviços. Stack .NET, time pequeno.
10. **Fronteira hub × consumidor:** telas, permissões de usuário final e regras de negócio do consumidor (ex.: comandas do VISU) ficam no sistema consumidor. O hub recebe emissão com dados fiscais resolvidos, valida, emite, guarda e expõe consulta/cancelamento/download/webhooks. Legado E-notas fica no VISU.

---

## 2. Arquitetura — módulos e fronteiras (Seção 1 v2)

> Esta seção incorpora os ajustes da revisão por 5 agentes especializados. Convergências que moldaram a v2: (a) a "extração limpa" exige eliminar três muletas invisíveis — transação de banco única, compilador validando contratos e outbox central; (b) numeração de série pertence à transação da Emissão; (c) o ciclo de vida precisa de máquina de estados fiscal real (contingência, denegada, eventos fiscais); (d) Emissão deve ser fina — conhecimento por tipo de documento vive no Motor; (e) tenant é fronteira de primeira classe; (f) 8 módulos físicos no dia 1 era pagar fronteira antes do valor.

### 2.1 Deployables

- **API** — REST pública para integradores; BFF do Portal do Emissor; emissão síncrona (caminho feliz com timeout).
- **Worker** — regularização de contingência; NFSe assíncrona (fase 2); entrega de webhooks; projeções de leitura; alertas (certificado, cota); exportações (fase futura).

### 2.2 Módulos do MVP (5)

| Módulo | Responsabilidades |
|---|---|
| **Contas & Planos** | Conta de integração; usuários do portal (identidade humana, MFA — separada das credenciais de máquina); API keys (hash, prefixo `nfh_live_`/`nfh_test_`, allowlist de empresas, rotação/revogação); planos e limites; metering (consumo por período/ambiente, alimentado por evento); configuração de webhooks, histórico de entregas e a **execução da entrega** (roda no Worker, consumindo eventos via inbox). |
| **Empresas & Certificados** | Empresas emissoras (CNPJ, IE, endereço fiscal, regime, `tpAmb`); **custódia** do certificado A1 e do CSC/idCSC (envelope encryption com AWS KMS, DEK por empresa; a chave privada **nunca sai do módulo** — o contrato expõe `AssinarXml(empresaId, xml) → xmlAssinado`); ciclo de validade do certificado (eventos `CertificadoProximoDoVencimento`/`CertificadoExpirado`); *configuração* de séries/faixas por modelo e ambiente (o contador não vive aqui). |
| **Emissão** | Máquina de estados fina e genérica do documento fiscal: `Rascunho → EmProcessamento → Autorizada \| Rejeitada \| Denegada \| EmContingencia`; `EmContingencia → Transmitida → Autorizada \| RejeitadaAposContingencia \| Denegada`; `Cancelada` só a partir de `Autorizada` (o evento exige `nProt`). `Denegada` é terminal e consome número. `RejeitadaAposContingencia` é incidente fiscal (DANFE já entregue ao consumidor): fila de tratamento no Portal — correção preservando `nNF`/`dhEmi`/`dhCont` e retransmissão — com webhook e alerta próprios. **Numeração:** transação curta e exclusiva que aloca e commita o número ANTES de montar/assinar/transmitir (contador por empresa+modelo+série+ambiente, lock pessimista que nunca atravessa I/O); o resultado da SEFAZ é persistido em transação subsequente. `Rejeitada` devolve o número a um pool do contador (reuso sob o mesmo lock — número rejeitado nunca foi autorizado); buracos residuais (crash) são detectados e inutilizados no prazo legal (dia 10 do mês subsequente). Eventos fiscais — cancelamento (NFC-e: 30 min da autorização, parametrizado por UF+modelo; extemporâneo fora do MVP — erro orienta requerimento à SEFAZ), inutilização de faixa (prazo legal validado), CC-e (NFe, fase futura): o estado/prazo é da Emissão; o XML/transmissão do evento é do Motor. Read model local `EmpresaLocal` (dados **cadastrais** estáveis replicados por evento — leituras de dados no caminho crítico só via `EmpresaLocal`; operações de custódia — validade de certificado, assinatura, QR Code — são sempre síncronas via contrato). Idempotência de recebimento obrigatória no endpoint de emissão (o mecanismo é o middleware do kernel; semântica em §3.1). |
| **Motor NFCe/NFe** | Gera o XML por tipo/UF (estratégia por estado; SE primeiro); monta QR Code conforme **NT 2025.001** — **v3 obrigatório em contingência** (assinatura digital, sem CSC), v2 (`p=chNFe\|2\|tpAmb\|cIdToken\|hash` com CSC) mantido no online enquanto a adoção do v3 online não for decidida — sempre via operação `GerarQrCode` de Empresas & Certificados (CSC/chave privada não saem da custódia); URL de consulta pública por UF+`tpAmb` configurável; solicita assinatura a Empresas & Certificados; transmite à SEFAZ — **`NFeAutorizacao4` (SVRS) com `indSinc=1` e lote unitário; a decisão é o `cStat` do `protNFe/infProt` (100 autorizada; 110/301/302 denegada), o `cStat` do lote é só transporte** — e **classifica o resultado** em `Autorizada \| RejeiçãoDefinitiva \| FalhaTransitória \| Denegada` (só `FalhaTransitória` autoriza retry); expõe `ConsultarStatus` (reconciliação por consulta é a fonte de verdade — nunca reenvio cego). Contratos de entrada definidos pelo próprio Motor (anti-corruption layer). É o módulo mais extraível e escalável (quase CPU-bound). |
| **Documentos** | XML autorizado + XMLs de eventos fiscais + DANFE/PDF no S3 (SSE-KMS, versionamento, Object Lock, retenção ≥ 5 anos, separado por ambiente); hash SHA-256; metering de storage por evento; **revalida tenant em toda leitura** (defesa em profundidade); URLs pré-assinadas de curta duração. Internamente dividido em Storage (estável) e Geração/Exportação (volátil) para permitir extração separada futura. |

**Módulos de fases futuras** (fronteira reservada no mapa; código só quando a fase chegar): **Motor NFSe** (dono da máquina RPS → conversão: `RpsEmitido → LoteEnviado → Convertida | Rejeitada | Cancelada` — NFSe não é "NFe com outro transporte"); **Cadastros Fiscais** (produtos/serviços, classes de imposto; validação fiscal *comum* entre documentos vive nele, validação *específica* vive em cada Motor); **Exportação Contábil**; **DF-e**; **Billing automático**.

**Cross-cutting kernel** (infraestrutura transversal, não módulo de negócio): tenant context (`ITenantContext` resolvido na borda; filtro global obrigatório em todo DbContext tenant-scoped; `BeginTenantScope(contaId)` obrigatório em todo job do Worker — job sem escopo de tenant é erro, não "processa tudo"); idempotência de recebimento na API; auditoria append-only (quem/quando/de onde/o quê/antes-depois) alimentada pelos eventos de integração, cobrindo ações administrativas sensíveis (troca de certificado, série, API key, webhook, plano); outbox/inbox como biblioteca instanciada por módulo.

### 2.3 Regras de fronteira

1. **Escrita isolada; leitura projetada.** Schema + DbContext por módulo; sem FK/join cross-módulo na escrita. Leitura cross-módulo permitida por um único mecanismo: **read models alimentados por eventos** (ex.: `NotaConsulta` serve listagens em 1 query). O caminho certo é o caminho fácil.
2. **Outbox POR MÓDULO; inbox por consumidor.** Tabela de outbox no schema de cada produtor; dispatcher é biblioteca instanciada por módulo (não serviço central). Handlers idempotentes (dedupe por `messageId`) e tolerantes a reordenação, **testados com duplicata e fora-de-ordem desde o monólito**. Trocar o transporte por RabbitMQ/SQS não altera módulos.
3. **Contratos versionados.** Comunicação síncrona só por interfaces de contrato público com DTOs próprios (nunca entidades); contratos de entrada do Motor pertencem ao Motor; evolução aditiva-só (nunca remover/renomear campo sem nova versão; consumidores toleram campos desconhecidos) — vale em dobro para eventos, que ficam persistidos.
4. **Identidade opaca.** `ContaId`/`EmpresaId` são GUIDs gerados pelo módulo dono, imutáveis, nunca reciclados; referenciados sem integridade referencial cross-módulo; existência/ciclo de vida propagados por eventos (`EmpresaCriada`/`EmpresaDesativada`), não por consulta síncrona a cada uso.
5. **Tenant é fronteira de primeira classe.** O isolamento entre contas vem do filtro transversal obrigatório + revalidação nos módulos sensíveis — não da fronteira de módulo (que é lógica). Teste de arquitetura falha o build se entidade tenant-scoped não tiver `conta_id` ou se um DbContext não registrar o filtro.
6. **Material sensível não transita.** A1 e CSC nunca saem de Empresas & Certificados — o cômputo do QR Code (hash v2 com CSC / assinatura v3) é operação do módulo de custódia (`GerarQrCode`, espelhando `AssinarXml`); eventos de integração carregam identificadores (`notaId`, `contaId`, `empresaId`, `status`, `chaveAcesso`), nunca XML/PDF/PII; webhooks assinados (HMAC) com validação anti-SSRF no registro de URL.
7. **Física espelha a lógica.** Projetos separados por módulo + NetArchTest no CI travando referências indevidas. Banco físico único com schemas separados no MVP; separação de banco por módulo só quando a escala pedir — o que é caro de retrofitar (outbox por módulo, inbox idempotente, identidade opaca, read models) já está pago pelas regras 1–6.

### 2.4 Decisões de fluxo estruturais

- **Emissão síncrona = só o caminho feliz.** A API tenta autorizar com timeout explícito no contrato do Motor (~5s); estouro ou indisponibilidade → estado `EmContingencia` (`tpEmis=9`). Entrar em contingência **regera o documento**: novo XML com `tpEmis=9` + `dhCont`/`xJust`, **nova chave de acesso**, nova assinatura e QR Code v3 — a chave original fica registrada na nota para reconciliação. O 202 devolve os **dados estruturados de impressão** (payload + QR v3 + dizeres obrigatórios; o PDV imprime em 2 vias) — sem PDF síncrono; Documentos segue fora do caminho crítico. No caso "enviou e não ouviu resposta", o Worker consulta a chave original (`ConsultarStatus`) **antes** de transmitir a via de contingência: se a original foi autorizada, ela é cancelada dentro do prazo — a via entregue ao consumidor prevalece. Transmissão da contingência em **até 24h** (prazo legal; alerta de primeira classe no §4.3). Contingência faz parte do primeiro commit, não do primeiro incidente.
- **Transmissão à SEFAZ é o ponto de não-retorno.** Operação não-idempotente por natureza: a Emissão persiste o resultado bruto da SEFAZ na própria transação antes de qualquer fan-out; caso ambíguo (enviou e não ouviu resposta) resolve por `ConsultarStatus` — é isso que impede nota duplicada hoje e na extração futura.
- **Documentos e metering fora do caminho crítico:** pós-commit, por evento (`NotaAutorizada` → Worker grava S3, incrementa consumo, dispara webhook).
- **Limite de plano = soft enforcement local.** Flags replicadas por evento (`CotaExcedida`, `ContaSuspensa`) checadas localmente pela Emissão (custo zero de rede). No MVP, cota excedida **sempre emite com `avisos`** — bloqueio exato no número N fica explicitamente adiado; se algum plano exigir, será desenhado como reserva síncrona. `ContaSuspensa` (origem: backoffice) bloqueia **escrita/emissão** com `403` + problem type `conta_suspensa`; consulta e download de documentos já emitidos permanecem (guarda legal).

### 2.5 Dependências permitidas (setas = "conhece o contrato de")

- Emissão → Empresas & Certificados (validade de certificado, fail-fast antes de emitir), Motor NFCe/NFe, Documentos (links), Cadastros Fiscais (fase futura, só quando a requisição referencia item cadastrado, com timeout/circuit breaker curto).
- Motor → Empresas & Certificados (somente `AssinarXml` e `GerarQrCode` via contrato — CSC não transita).
- API/Portal → módulos via contratos; listagens via read model `NotaConsulta`.
- Nenhum **módulo de negócio** → Contas & Planos (credencial resolvida uma vez na borda; `conta_id` + escopo de empresas propagados como contexto/claim; consumo e cota chegam por evento). O host API e o middleware de autenticação do kernel consomem apenas o contrato público de credencial/consumo/webhook-config — exceção codificada na suite NetArchTest.

---

## 3. Contrato da API pública e fluxos (Seção 2)

### 3.1 Princípios

REST versionada por path (`/v1`), JSON, erros em RFC 7807 (Problem Details) com `traceId`. Autenticação por API key (`Authorization: ApiKey nfh_live_...` / `nfh_test_...`); key de teste só opera empresas em homologação. Portal usa identidade própria (JWT + sessão), nunca API key. `Idempotency-Key` aceita em toda escrita e **obrigatória na emissão**: reenvio por timeout devolve o mesmo resultado; mesma key com payload diferente → 409.

### 3.2 Recursos

```
POST   /v1/empresas                          cria empresa emissora
POST   /v1/empresas/{id}/certificado         upload A1 (PFX + senha) → custódia KMS
PUT    /v1/empresas/{id}/series/{modelo}     configura série/faixa por ambiente
POST   /v1/empresas/{id}/csc                 cadastra CSC/idCSC (NFC-e)

POST   /v1/nfce                              emite NFC-e (síncrona; Idempotency-Key obrigatória)
GET    /v1/nfce/{id}                         detalhe + status
POST   /v1/nfce/{id}/cancelamento            cancela (valida prazo legal)
POST   /v1/inutilizacoes                     inutiliza faixa de numeração
GET    /v1/nfce/{id}/xml | /danfe            download (URL S3 pré-assinada)
GET    /v1/notas?empresaId=&status=&de=&ate= listagem paginada (read model)

PUT    /v1/webhooks                          endpoint + eventos (HMAC)
GET    /v1/consumo                           uso do plano no período
```

### 3.3 Emissão de NFC-e — request essencial

```json
POST /v1/nfce   (Idempotency-Key: pdv-42-venda-98765)
{
  "empresaId": "guid",
  "consumidor": { "cpf": "opcional", "nome": "opcional" },
  "itens": [{
    "descricao": "Pomada modeladora 120g", "quantidade": 1, "valorUnitario": 45.00,
    "fiscal": { "ncm": "3305.90.00", "cfop": "5102", "origem": 0, "cst": "102",
                "cstPis": "99", "cstCofins": "99" }
  }],
  "pagamentos": [{ "tipo": "pix", "valor": 45.00 }],
  "observacoes": "opcional"
}
```

O integrador envia dados fiscais resolvidos. Fase futura: `itemCadastradoId` no lugar do bloco `fiscal` (módulo Cadastros Fiscais).

### 3.4 Respostas — espelham a máquina de estados

| HTTP | Situação | Corpo |
|---|---|---|
| 201 | `autorizada` | chave de acesso, número/série, protocolo, links `xml`/`danfe`, dados do QR Code |
| 202 | `em_contingencia` | número/série alocados, **chave de contingência** (`tpEmis=9`), dados estruturados de impressão da DANFE de contingência (payload + QR Code v3 + dizeres obrigatórios; o PDV imprime em 2 vias), regularização posterior via webhook |
| 422 | `rejeitada` | problem details com `cStat`, mensagem SEFAZ, campo ofensor quando identificável — não re-tentar igual |
| 422 | `denegada` | terminal; número consumido |
| 400 | validação do hub | erros por campo, antes de tocar a SEFAZ |
| 409 | `Idempotency-Key` reutilizada com payload diferente | conflito explícito |
| 403 | `conta_suspensa` (origem: backoffice) | bloqueia emissão/escrita; consulta e download permanecem (guarda legal) |

Mapeamento da classificação do Motor: `RejeiçãoDefinitiva`→422; `FalhaTransitória`→202 (contingência); erro de infra do hub→500 com `traceId`. Cota excedida no MVP **não bloqueia**: emite com `avisos: ["cota_excedida"]` (um 429 por política de conta é comportamento pós-MVP — ver §5). Os links `xml`/`danfe` do 201 são os endpoints do hub (§3.2): `/xml` serve o XML autorizado direto da Emissão desde o 201; `/danfe` responde `202 Retry-After` até a gravação pós-commit no S3 — URLs S3 pré-assinadas só no momento do download.

### 3.5 Fluxo síncrono com contingência

```
PDV → POST /v1/nfce
  API: valida → aloca número (transação curta, commit) → Motor: monta + assina
  Motor → SEFAZ-SE (indSinc=1, timeout ~5s)
  ├─ autorizou (protNFe/cStat=100) → persiste resultado bruto → 201 → outbox: NotaAutorizada
  │               └─ Worker (pós-commit): XML/PDF → S3, metering, webhook
  └─ timeout/indisponível → EmContingencia: regera XML (tpEmis=9, dhCont/xJust,
      nova chave, reassina, QR v3) → 202 com dados de impressão → venda não para
      → enviou sem resposta? Worker consulta a chave ORIGINAL primeiro:
        autorizada → cancela a original no prazo; a via de contingência prevalece
      → Worker transmite a contingência em até 24h (alerta de 1ª classe)
        ├─ autorizada → webhook nota.contingencia_regularizada
        └─ rejeição definitiva/denegada → RejeitadaAposContingencia
           → webhook nota.contingencia_rejeitada + fila de tratamento no Portal
```

### 3.6 Webhooks

Eventos: `nota.autorizada`, `nota.rejeitada`, `nota.cancelada`, `nota.contingencia_regularizada`, `nota.contingencia_rejeitada`, `certificado.expirando` (30/15/7/1 dias), `cota.proxima_do_limite`, `cota.excedida`. Payload mínimo (ids, chave, status, timestamp — nunca XML/PII). Entrega **at-least-once e sem garantia de ordem** (contrato explícito: o integrador reconcilia por status/timestamp ou por `GET /v1/nfce/{id}`). Header `X-NFH-Signature` (HMAC-SHA256 por conta + timestamp anti-replay). Retry com backoff exponencial por ~24h; ao esgotar, a entrega fica `esgotada` no histórico (redisparo manual no portal); endpoint com falha contínua por dias é desativado com notificação. Registro de URL validado contra SSRF (bloqueio de IP privado/link-local/loopback, HTTPS obrigatório, revalidação de DNS na entrega).

### 3.7 Ambiente (homologação × produção)

`tpAmb` é atributo de primeira classe da empresa — a empresa tem um **ambiente corrente** (escalar; a virada homologação→produção é transição explícita), enquanto CSC, séries e contadores são mantidos **por ambiente** para que a virada não perca configuração. Permeia tudo: URL de webservice, CSC, numeração (contadores separados), guarda (homologação dispensa retenção de 5 anos), metering (**só produção conta para o plano**) e escopo de API key (`nfh_test_` só opera empresa com ambiente corrente = homologação).

---

## 4. Modelo de dados, Portal, observabilidade e testes (Seção 3)

### 4.1 Modelo de dados (por schema)

Convenções: PKs GUID geradas pelo módulo dono; `conta_id` em toda tabela tenant-scoped; auditoria (timestamps + RowVersion); sem delete físico de dado fiscal; **todo schema de módulo produtor/consumidor tem seu par `Outbox`/`Inbox`** (§2.3.2 — listados abaixo apenas em `emissao` por brevidade).

- **`contas`** — `Conta`, `UsuarioPortal`, `ApiKey` (hash, prefixo, ambiente, allowlist de empresas, revogação), `Plano`, `AssinaturaConta`, `ConsumoPeriodo` (agregado mês/ambiente, por evento), `WebhookConfig`, `WebhookEntrega`.
- **`empresas`** — `Empresa` (CNPJ, IE, endereço fiscal, regime, `tpAmb`), `Certificado` (PFX cifrado, DEK por empresa/KMS, validade, status), `CscConfig` (cifrado, por ambiente), `SerieConfig` (modelo, série, faixa, ambiente).
- **`emissao`** — `NotaFiscal` (estado, tipo, número/série/ambiente, chave — e chave original quando regerada em contingência —, `dhCont`/`xJust`, protocolo, `cStat`, **snapshot imutável do payload de emissão**), `EventoFiscal` (cancelamento/inutilização/CC-e, com protocolo e estado próprios), `ContadorNumeracao` (empresa+modelo+série+ambiente, lock na transação curta + pool de números liberados por rejeição), `EmpresaLocal` (read model), `Outbox`, `Inbox`.
- **`documentos`** — `Documento` (tipo: xml-autorizado/xml-evento/danfe-pdf, chave S3, hash SHA-256, ambiente, tamanho).
- **`leitura`** — `NotaConsulta` (projeção desnormalizada de **propriedade da Emissão** — dona da escrita do schema, alimentada por eventos: nota + empresa + links + status; serve `GET /v1/notas`, Portal e cards de resumo em 1 query).

### 4.2 Portal do Emissor (MVP) e Backoffice

**Portal:** onboarding da empresa; upload do A1 com validação imediata (senha, titularidade CNPJ, validade) e banner de expiração; série e CSC por ambiente; consulta de notas com filtros, download XML/DANFE, cancelamento manual (prazo validado); consumo do plano; webhooks (config, histórico, redisparo); usuários da conta com papel por empresa (admin da conta × operador da empresa X).

**Backoffice interno:** aplicação separada; identidade de operador com MFA + RBAC + auditoria reforçada (nunca o pipeline de API key). Habilitação de contas, planos, visão de saúde por tenant (contingências, certificados vencendo, rejeições recorrentes), suporte.

### 4.3 Observabilidade

Logs estruturados sem PII (CPF/chave mascarados); `CorrelationId` de ponta a ponta (request → outbox → worker → webhook); OpenTelemetry. Métricas nucleares: taxa autorização × rejeição por `cStat`; latência p50/p99 SEFAZ-SE; **notas em contingência agora e a vencer o prazo legal de 24h de transmissão** (alerta nº 1); tempo até regularização; profundidade da fila do Worker; falha de webhook por conta; certificados a vencer; consumo × limite. Health: `/alive` (processo) separado de `/health` (banco, S3, KMS; SEFAZ como *degraded* — SEFAZ fora não derruba o hub, ativa contingência).

### 4.4 Testes

| Camada | Cobertura | Ferramenta |
|---|---|---|
| Unidade | Máquina de estados completa (incl. Denegada/EmContingencia/RejeitadaAposContingencia), numeração sob concorrência (incl. pool de rejeitadas), prazos (cancelamento 30 min, contingência 24h, inutilização dia 10), classificação pelo `cStat` do `protNFe` | xUnit, sem I/O |
| Golden files | XML do Motor comparado com amostras validadas contra XSD da SEFAZ — incl. contingência (`tpEmis=9`, `dhCont`/`xJust`) e QR v2/v3; assinatura (posicionamento de `<Signature>` irmã de `infNFe`) com certificado de teste | xUnit |
| Arquitetura | Regras de fronteira §2.3 (referências, filtro de tenant em todo DbContext, `conta_id` obrigatório em entidade tenant-scoped) | NetArchTest no CI |
| Integração | Fluxo completo com banco real + SEFAZ fake (autorização, rejeição por `cStat`, timeout → contingência, regularização rejeitada, caso ambíguo reconciliado por `ConsultarStatus`); handlers com eventos duplicados/fora de ordem; isolamento de tenant (conta A jamais lê dado da conta B) | Testcontainers |
| E2E/smoke | Emissão real na homologação SEFAZ-SE com certificado de teste, no pipeline de release | Cenários críticos |

### 4.5 Deploy (nível de design)

2 containers (API, Worker) na AWS; RDS; S3; KMS. Migrations por módulo/schema. Topologia de ambientes: **um ambiente produtivo do hub** — o estágio "homologação" do pipeline usa empresas com `tpAmb=2` nesse ambiente (sem staging separado no MVP; decisão em `docs/decisions.md`). Backup: RDS com PITR e teste de restore no runbook — **restore rebobina `ContadorNumeracao`: reconciliar por `ConsultarStatus` antes de reabrir emissão**. Pipeline: build → unidade+arquitetura → integração → homologação (`tpAmb=2`) → smoke SEFAZ-SE → produção. Detalhamento de infra no plano de implementação.

---

## 5. Fora de escopo do MVP (explícito)

NFSe (fase 2 — módulo Motor NFSe nasce lá), NFe (fase 3), DF-e, importação de XML de compra, Cadastros Fiscais, exportação contábil, billing automático/gateway de pagamento, EPEC, CC-e, enforcement hard de cota (429 por política de conta), exportação em massa de documentos no offboarding, cancelamento extemporâneo de NFC-e (requerimento à SEFAZ), separação física de banco por módulo, extração de microsserviços.

**Encerramento de conta (política mínima do MVP):** conta cancelada entra em somente-leitura — consulta e download dos documentos permanecem pelo prazo de guarda legal (5 anos); A1 e CSC são destruídos de forma auditada no cancelamento; exportação em massa fica fora do MVP (acima).

**LGPD:** o hub atua como **operador** dos dados de consumidor (CPF/nome) enviados pelo integrador (controlador); base legal da retenção é a obrigação fiscal — apagamento negado durante o prazo de guarda, com resposta padrão a requisições de titular; DPA nos termos comerciais é pré-requisito da Fase 5 (primeiro integrador externo); demais obrigações ficam fora do escopo técnico do MVP.

## 6. Riscos principais e mitigação

| Risco | Mitigação no design |
|---|---|
| Nota duplicada / buraco de numeração | Numeração na transação da Emissão; reconciliação por `ConsultarStatus`; `Idempotency-Key` obrigatória |
| SEFAZ-SE indisponível no caixa | Contingência offline como caminho de primeira classe (202 + DANFE), Worker regulariza |
| Certificado A1 expira em silêncio | Ciclo de validade com eventos/alertas 30/15/7/1 dias; fail-fast antes de assinar |
| Vazamento entre tenants | Filtro global obrigatório + `BeginTenantScope` no Worker + revalidação em Documentos + testes de arquitetura |
| Vazamento do A1/CSC | Custódia em módulo único, KMS, contrato de operação (`AssinarXml`), auditoria de acesso |
| Fronteiras furadas sob pressão | Read models como caminho fácil e legal para leitura cross-módulo; NetArchTest no CI |
| Homologação contaminando produção | `tpAmb` de primeira classe em numeração, guarda, metering e escopo de API key |
| Contingência além do prazo de 24h / rejeitada na regularização | Alerta de 1ª classe (§4.3); estado `RejeitadaAposContingencia` com fila de tratamento no Portal e webhook próprio |
| Restore de backup rebobinando numeração | PITR + runbook de restore com reconciliação por `ConsultarStatus` antes de reabrir emissão |

## 7. Próximos passos

1. Plano de implementação (skill writing-plans) derivado deste design.
2. Após o plano: análise do código existente do repositório (arquitetura, escalabilidade, qualidade) contra este design — decisão de aproveitar/adaptar/reescrever por módulo.
