# Handoff — Gate 0 em andamento (sessão interrompida por instabilidade 529 da API)

**Data:** 2026-07-02
**Como retomar:** `/resume gate 0` — ler este arquivo primeiro; ele contém o estado completo e os relatórios recebidos.

---

## 1. O que foi feito nesta sessão (concluído e commitado)

1. **Contexto Jira:** épico VISX-1188 + 13 histórias filhas lidos e consolidados (as histórias são majoritariamente o lado consumidor/VISU; o hub é produto independente).
2. **Design do produto** (brainstorming completo, 3 seções aprovadas pelo dono do produto, revisado por time de 5 agentes na Seção 1): `docs/superpowers/specs/2026-07-01-nota-fiscal-hub-produto-design.md` — commit `104ad8f`.
3. **Plano-mestre do MVP** (Gate 0 + Fases 0–7): `docs/superpowers/plans/2026-07-01-nota-fiscal-hub-mvp-plano-mestre.md` — commit `74780de`.
4. **Gate 0 iniciado:** time de 5 agentes revisando o código existente contra o design. **4 de 5 relatórios recebidos** (resumos completos na seção 3 abaixo). O 5º (qualidade/testes) falhou 3× com API 529 Overloaded; dados parciais já coletados manualmente (seção 4).

## 2. Decisões de produto/arquitetura já fixadas (não reabrir)

- Motor emissor próprio (não wrapper); roadmap NFC-e SE → NFSe → NFe; conta 1..N empresas; API + Portal; síncrono p/ NFC-e com contingência, assíncrono p/ NFSe; planos desde o dia 1 + metering (soft enforcement); monólito modular 5 módulos + kernel, 2 deployables, regras de fronteira §2.3 do spec.
- Estrutura real do repo: solution 4 camadas (`VisuFiscalHub.{Api,Application,Domain,Infrastructure}` + `Tests`), 1 deployable, DbContext único — NÃO é o monólito modular do design.

## 3. Relatórios do Gate 0 recebidos (essência completa)

### 3.1 g0-aderencia (dotnet-architect) — recomendação: REESTRUTURAR a solution, não evoluir in-place
- Mapa: `ClienteApp`≈Conta (fração pequena; sem planos/metering/MFA); `Tenant` FUNDE Empresa+Certificado+CSC+Série (nomenclatura invertida: Tenant deveria ser a conta); `DocumentoFiscal` = god entity NFC-e+NFe+NFSe com ifs por tipo; Motor disperso em `Infrastructure/Fiscal/*` (maior ativo); Documentos/S3 não existe (XML embutido no agregado).
- Kernel: outbox central único bem-feito (`DomainEventsInterceptor`+`OutboxRelayJob` SKIP LOCKED) mas sem Inbox; idempotência embutida no handler de emissão; SEM filtro global de tenant; SEM auditoria; SEM Worker separado (Hangfire in-process na API, `Program.cs:598`); SEM NetArchTest.
- Matriz: Contas&Planos REESCREVER(M); Empresas&Certificados ADAPTAR c/ quebra de agregado(G); Emissão REESCREVER núcleo/ADAPTAR casca(G); Motor NFCe/NFe ADAPTAR(M) — maior reaproveitamento; Documentos REESCREVER(M); Outbox/Inbox ADAPTAR(M); TenantContext REESCREVER(P/M); Idempotência REESCREVER(P); Auditoria REESCREVER(M); Deployables REESCREVER(P/M).
- Estratégia: nova solution modular (Fase 0), portar cirurgicamente `Infrastructure/Fiscal/{Sefaz,XmlBuilder}` + `QrCodeGenerator` + testes para o Motor; portar `CertificateEncryptionService` redesenhando agregado; NFSe ABRASF não portar agora (fase 2); Clean Architecture entre camadas atual está correta (setas de referência verificadas) e os padrões (VOs, Result<T>, eventos, CQRS) viram convenção.

### 3.2 g0-fiscal — 3 bugs CRÍTICOS impedem qualquer emissão real hoje
1. **cStat do nível errado**: `SefazRetornoParser.cs:51` lê `retEnviNFe/cStat` mas envelope usa `indSinc=1` → autorização real vem em `protNFe/infProt/cStat=100`; parser vê 104 do lote → `Autorizado` sempre false. `nProt`/`protNFe` idem.
2. **`<Signature>` dentro de `infNFe`** (`XmlSigner.cs:51-55` AppendChild no elemento com Id alvo): schema exige irmã de `infNFe`, filha de `<NFe>` → rejeição 215.
3. **Mesmo bug no evento de cancelamento** (assinatura dentro de `infEvento`; deve ser filha de `<evento>`) → cancelamento nunca confirma.
- ALTO: numeração `seq_nfe_{tenant}_{serie}` sem ambiente(tpAmb) nem modelo(55/65), não-transacional, não libera número em Rejeitada; contingência inexistente (tpEmis hardcoded "1", sem estado EmContingencia); emissão 100% assíncrona (design exige síncrona 201/202/422 com timeout ~5s).
- MÉDIO: prazo cancelamento NFC-e 30min hardcoded (validar SE); inutilização não existe; **QR Code sem `cIdToken`** na URL e no hash (NT 2015.002: `p=chave|2|tpAmb|cIdToken|cHash`, hash=SHA1(chave|2|tpAmb|cIdToken|CSC)) — `Tenant.CIdToken` existe mas não é passado (`QrCode.cs:41,46`); `aamm` da chave em UTC vs `dhEmi` no fuso da UF → rejeição 502 na virada de mês.
- CORRETO/aproveitável: chave de acesso+DV módulo 11 (`ChaveAcesso.cs`), taxonomia cStat do parser (Denegado/Duplicidade/Recuperável), máquina de estados (menos contingência), `SefazEndpointResolver` (SE→SVRS correto), idempotência+commit-antes-de-enfileirar.
- Prioridade p/ destravar homologação: #1 cStat → #2/#3 assinatura → cIdToken QR → numeração ambiente/modelo transacional.
- Matriz: XML/chave APROVEITAR; QR ADAPTAR; Assinatura REESCREVER posicionamento; Transporte ADAPTAR; cStat/estados APROVEITAR; Contingência REESCREVER(implementar); Cancelamento ADAPTAR+inutilização IMPLEMENTAR; Numeração REESCREVER; NFSe REESCREVER(fase 2).

### 3.3 g0-escalabilidade — mecânica madura, MODELO de fluxo diverge do design
- CRÍTICO: sem síncrono/contingência (endpoints sempre 202 `Program.cs:434-531`; SEFAZ fora = venda presa ~1h20 de backoffs); numeração via sequence: atômica (sem duplicata — bom) mas com buracos permanentes e sem escopo ambiente/modelo; inutilização inexistente.
- ALTO: isolamento tenant 100% manual (zero `HasQueryFilter`; jobs de varredura sem tenant scope — `ReconciliacaoJobProcessor.cs:53-68`); **webhook entregue INLINE dentro da transação do OutboxRelay** (HTTP 15s segurando FOR UPDATE de 50 linhas; commit falha → webhook duplicado; sem inbox/dedupe).
- MÉDIO: 1 processo API+Worker, fila única Hangfire 4 workers → head-of-line blocking entre tenants; reconciliação serial sem paginação (estoura lock 120s sob backlog); `SefazHttpClient` cria handler/TLS por chamada, sem Polly, timeout 30s (incompatível com orçamento síncrono ~5s).
- BAIXO: listagem de tenant carrega PFX cifrado sem projeção; corrida de idempotência devolve 500 em vez do resultado (índice único protege).
- Matriz: Numeração ADAPTAR(decisão de produto: sequence+inutilização vs ContadorNumeracao); Outbox ADAPTAR(+inbox, webhook em job próprio, por-módulo depois); Fluxo síncrono REESCREVER; Worker ADAPTAR(separar deployable, filas por criticidade); Queries APROVEITAR(+projeções/read model); Tenant REESCREVER(fundação).

### 3.4 g0-seguranca — nada trivialmente explorável hoje; 2 riscos estruturais ALTOS
- ALTO: A1+senha cifrados com UMA chave AES estática de config (`CertificateEncryptionService.cs:16-25`, env `CERT_ENCRYPTION_KEY`) p/ todos os tenants, sem KMS/DEK — chave+dump do banco = todos os certificados; chave privada trafega como `X509Certificate2` entre camadas (`TenantCertificateProvider`→`XmlSigner`/`SefazHttpClient`) em vez de confinada atrás de `AssinarXml`; sem filtro global de tenant (isolamento manual consistente HOJE, mas bomba-relógio); jobs varrem todos os tenants sem escopo.
- MÉDIO: upload de certificado NÃO valida senha/titularidade CNPJ/validade real (Vencimento é cliente-controlado; falha só aparece na 1ª emissão); parse XML sem hardening explícito anti-XXE (mitigado por default do .NET; centralizar `XmlResolver=null`+`DtdProcessing.Prohibit`).
- BAIXO: `Type.GetType` no OutboxRelay a partir de coluna (whitelist recomendada); AdminKey = segredo único estático (stop-gap aceitável).
- FORTE (aproveitar): PBKDF2 600k+FixedTimeEquals; JWT RS256 estrito; anti-SSRF de webhook exemplar (blocklist+DNS pinning+HMAC); logs sem PII; SQL raw seguro; Dockerfile multi-stage não-root; CSC cifrado.
- Gap de processo: **não existe CI/CD** (`.github/` sem workflows).
- Matriz: Certificados REESCREVER; Tenant ADAPTAR(kernel novo, manter checagens como defesa em profundidade); Auth APROVEITAR(evoluir p/ nfh_live_/test_ depois); PII APROVEITAR; Injeção ADAPTAR; Infra ADAPTAR(criar CI).

## 4. g0-qualidade — PENDENTE (falhou 3× com 529) + dados já coletados manualmente

- **A suíte de testes NÃO COMPILA**: `tests/.../Api/GlobalExceptionHandlerTests.cs:17,40` — CS7036, construtor de `GlobalExceptionHandler` ganhou `IHostEnvironment` e os testes não foram atualizados. Sem CI, ninguém viu.
- **NU1903 (vulnerabilidade alta conhecida)**: `Microsoft.OpenApi 2.0.0` (GHSA-v5pm-xwqc-g5wc) e `System.Security.Cryptography.Xml 10.0.0` (GHSA-37gx-xxp4-5rgx, GHSA-w3x6-4m5h-cxqf) — este último é o pacote da assinatura fiscal.
- Suíte: 53 arquivos, **363 casos** ([Fact]+[Theory]): Domain 89, Infrastructure 180, Integration 60, Application 24, Api 10.
- FALTA (retomar): rodar a suíte após corrigir a compilação (ou excluir o arquivo quebrado) p/ pass/fail real; varredura de silent failures (catch genérico/vazio, .Result/.Wait/async void); avaliar cobertura real vs §4.4 do spec (concorrência de numeração? golden files XSD? isolamento de tenant? duplicata/reordenação de eventos?); nota 0-10 por camada.

## 5. Matriz preliminar consolidada (a validar com o relatório de qualidade)

| Área | Decisão | Fonte |
|---|---|---|
| Motor NFCe/NFe (XML, SEFAZ, QR) | **ADAPTAR** — maior ativo; corrigir 3 críticos fiscais + cIdToken | aderencia+fiscal |
| Empresas & Certificados | **ADAPTAR estrutura / REESCREVER custódia** (quebra de Tenant; KMS/DEK; AssinarXml) | aderencia+seguranca |
| Emissão (núcleo: numeração, estados, síncrono/contingência) | **REESCREVER** | todos |
| Contas & Planos | **REESCREVER** (só ClienteApp existe) | aderencia |
| Documentos (S3, retenção, read model) | **REESCREVER** (não existe) | aderencia |
| Kernel tenant/idempotência/auditoria/NetArchTest | **REESCREVER** (fundação Fase 0) | aderencia+seguranca+escalabilidade |
| Outbox (mecânica) | **ADAPTAR** (+Inbox, webhook fora do relay, por-módulo) | aderencia+escalabilidade |
| Auth/JWT/hash/SSRF/logs | **APROVEITAR** | seguranca |
| NFSe ABRASF | **NÃO PORTAR agora** (fase 2; scaffold sem assinatura) | aderencia+fiscal |
| Estratégia geral | **Reestruturar solution nova (Fase 0) com porte cirúrgico do Motor e padrões como convenção** | convergência |

## 6. Próximos passos (ordem)

1. Concluir a revisão de **qualidade/testes** (relançar agente quando a API estabilizar, ou fazer inline — os dados da seção 4 já adiantam metade).
2. **Consolidar a matriz final do Gate 0** (5 pareceres) e apresentar ao dono do produto para decisão formal (critério de saída do Gate 0 no plano-mestre).
3. Registrar a decisão de estratégia em `docs/decisions.md`.
4. Ajustar o plano-mestre com o resultado (Fase 3 encolhe onde o Motor for portado; adicionar correções fiscais críticas #1-#3 + cIdToken como itens explícitos).
5. Detalhar a **Fase 0** com superpowers:writing-plans e iniciar execução.

## 7. Observações operacionais

- Time de agentes da sessão: g0-aderencia, g0-fiscal, g0-escalabilidade, g0-seguranca (entregues); g0-qualidade e g0-qualidade-2 (falharam com 529 — relançar com o prompt do item 4 da seção "FALTA").
- O prompt completo dos agentes do Gate 0 está registrado no histórico da sessão anterior; o essencial está reproduzido na seção 4 (FALTA).
- Dossiê da revisão de design (Seção 1) em: scratchpad da sessão anterior (descartável; o resultado já está incorporado no spec).
