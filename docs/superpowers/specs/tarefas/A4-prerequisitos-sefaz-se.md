# Spec A4 — Pré-requisitos externos SEFAZ-SE (certificado, CSC, credenciamento, XSDs)

> Card: https://app.clickup.com/t/86e24c1bv | Grupo A | Gerada em 2026-07-02 por time de agentes (escritor especialista + revisor adversarial)

## Objetivo

Destravar a trilha externa do Gate 0: obter os quatro insumos que NÃO são código e que têm lead time fora do nosso controle — (1) certificado A1 de teste, (2) CSC/idCSC de homologação, (3) credenciamento do emissor na SEFAZ-SE e (4) pacotes de schemas XSD — para que as Fases 2/3 (assinatura, QR Code, transmissão, golden files da Fase 3) não fiquem bloqueadas por burocracia quando o código estiver pronto. O credenciamento de **produção** INICIA aqui, como pré-requisito nomeado da Fase 7 (plano-mestre, Gate 0 e Fase 7).

## Contexto e referências

- Design: `docs/superpowers/specs/2026-07-01-nota-fiscal-hub-produto-design.md` — §4.4 (golden files vs XSD; E2E/smoke em homologação SEFAZ-SE com certificado de teste), §3.7 (`tpAmb` de primeira classe), regra 6 (A1/CSC nunca saem da custódia).
- Plano-mestre: `docs/superpowers/plans/2026-07-01-nota-fiscal-hub-mvp-plano-mestre.md` — Gate 0 (item "pré-requisitos externos"), Fase 3 (homologação ponta a ponta), Fase 7 (smoke `tpAmb=2`; credenciamento de produção iniciado no Gate 0). Global Constraints valem integralmente (A1 somente; QR v2/v3 NT 2025.001; `indSinc=1`).
- Decisões: `docs/decisions.md` §0 — D-2026-07-01-01 (QR v3 obrigatório em contingência ⇒ XSDs/NT 2025.001 no pacote), D-2026-07-01-07 (um ambiente produtivo; "homologação" = empresas `tpAmb=2`), D-2026-07-01-09 (CSC/séries por ambiente ⇒ CSC de homologação é registro próprio, distinto do de produção).
- Handoff: `docs/superpowers/handoffs/2026-07-01-revisao-docs-e-gate0-handoff.md` — regra SÓ-LEITURA no código de produção durante o gate.
- Fato técnico: Sergipe (cUF=28) autoriza NFC-e via **SVRS** (grupo autorizador), mas cadastro de contribuinte, credenciamento e geração de CSC são atos da **SEFAZ-SE** (estado do emissor). Diferente do sandbox AM, a homologação SVRS exige certificado ICP-Brasil real e emissor credenciado.

## Escopo (dentro / fora)

**Dentro:** os 4 blocos abaixo, executáveis em paralelo entre si e em paralelo ao grupo B; registro de evidências e de datas em `docs/decisions.md` §0 (ou anexo do card); guarda segura dos materiais sensíveis fora do repositório.
**Fora:** qualquer código (regra só-leitura do gate); upload do A1/CSC no hub (Fase 2); emissão em homologação (Fase 3); conclusão do credenciamento de produção e virada `tpAmb=1` (Fase 7); certificado A1 de produção de cliente (onboarding); NFSe/prefeituras.

## Abordagem / Passos

### Bloco 1 — Certificado A1 de teste (responsável: dono do produto)

1. Definir o titular: **e-CNPJ A1** da empresa que será o emissor piloto em homologação (a validação de titularidade do upload na Fase 2 compara CNPJ do certificado × CNPJ da empresa — o certificado deve ser do mesmo CNPJ que será credenciado no Bloco 3).
2. Adquirir A1 (arquivo PFX/PKCS#12 + senha) junto a uma **Autoridade Certificadora credenciada na ICP-Brasil** (canal: site de qualquer AC credenciada; requer videoconferência ou atendimento presencial para validação do representante legal). A3/token está proibido pelas Global Constraints.
3. Armazenar o PFX e a senha **fora do repositório**, em cofre de segredos do dono (o repo nunca versiona PFX/senha; a custódia definitiva será o módulo Empresas & Certificados com KMS, Fase 2).
4. Registrar no card: AC emissora, CNPJ titular, data de validade (alerta se < 12 meses — cobre Fases 2–7).

- **Entrada:** CNPJ ativo do emissor piloto + representante legal disponível. **Saída:** PFX + senha em cofre; validade registrada.
- **Prazo típico:** 1–3 dias úteis após a validação de identidade. **Risco:** agenda de videoconferência da AC; certificado emitido para CNPJ errado (conferir antes de pagar).

### Bloco 2 — CSC/idCSC de homologação (responsável: dono do produto; depende do Bloco 3-homologação em SE)

1. Obter o par **CSC (código) + idCSC (identificador de 6 dígitos)** do ambiente de **homologação**, no canal de atendimento eletrônico ao contribuinte da SEFAZ-SE (área autenticada do portal estadual — o acesso normalmente usa o próprio certificado digital do Bloco 1; alguns estados delegam a geração ao portal do autorizador SVRS — verificar no atendimento da SEFAZ-SE qual é o canal vigente).
2. Confirmar que o par é do **ambiente de homologação** (`tpAmb=2`) — CSC é segregado por ambiente (D-2026-07-01-09); o de produção é outro registro, obtido na Fase 7.
3. Armazenar CSC/idCSC no mesmo cofre do Bloco 1 (nunca no repo; no hub ele viverá cifrado em `CscConfig`).

- **Entrada:** emissor com acesso à área autenticada da SEFAZ-SE (em geral pós-credenciamento de homologação). **Saída:** par CSC+idCSC de homologação no cofre, com print/protocolo da geração.
- **Prazo típico:** imediato (autosserviço) uma vez habilitado o acesso. **Risco:** confundir ambiente (CSC de produção não valida QR em homologação → QR inválido silencioso); revogar/regerar o CSC invalida QR Codes já computados.

### Bloco 3 — Credenciamento do emissor na SEFAZ-SE (responsável: dono do produto; contador pode ser exigido)

1. **Homologação (agora):** solicitar o credenciamento do CNPJ piloto como emissor de NFC-e em ambiente de teste/homologação, pelo canal oficial da SEFAZ-SE (portal estadual / atendimento ao contribuinte; em SE o rito pode ser autosserviço na área autenticada ou processo via e-mail/protocolo — o dono verifica o rito vigente diretamente no atendimento). Pré-condições usuais: inscrição estadual ativa em SE e certificado do Bloco 1.
2. **Produção (INICIA já, conclui na Fase 7):** abrir no mesmo canal o pedido de credenciamento de produção. É pré-requisito nomeado da Fase 7 — não é bloqueante do Gate 0/Fase 3, mas o relógio começa aqui.
3. Registrar protocolos e datas no card e em `docs/decisions.md` §0 (rastro para o go-live).

- **Entrada:** CNPJ com IE ativa em SE + certificado A1. **Saída:** emissor apto a transmitir em homologação SVRS (status confirmado pela SEFAZ-SE) + protocolo do pedido de produção aberto.
- **Prazo típico:** de imediato a ~2 semanas, conforme o rito estadual e pendências cadastrais. **Risco principal do grupo A: credenciamento demora e é externo — iniciar no primeiro dia**; IE irregular trava tudo (e em produção geraria `cStat=110` Denegada).

### Bloco 4 — Pacotes de schemas XSD para os golden files da Fase 3 (responsável: agente, com verificação do dono)

1. Baixar do **Portal Nacional da NF-e (seção de documentos/schemas)** — canal oficial mantido pelo ENCAT/SEFAZ-Virtual — os pacotes vigentes: (a) **Pacote de Liberação (PL) da NF-e/NFC-e** compatível com o layout 4.00 aceito pela SVRS para SE (inclui `leiauteNFe`, `nfe`, `procNFe`, evento de cancelamento e inutilização); (b) schemas/anexos da **NT 2025.001** (QR Code v3, obrigatório em contingência — D-2026-07-01-01); (c) a própria NT 2025.001 e o MOC vigente como documentação de apoio.
2. Conferir integridade: versão do pacote, data de publicação e correspondência com a versão de schema anunciada pela SVRS para homologação (nota técnica/aviso no portal do autorizador).
3. **Durante o gate (só-leitura): não commitar nada em código.** Guardar os pacotes no scratchpad/pasta local acordada e registrar no card versão + origem + hash SHA-256 de cada zip. A incorporação como assets de teste dos golden files é tarefa da Fase 3.

- **Entrada:** acesso ao portal nacional. **Saída:** zips dos XSDs + NT 2025.001 arquivados localmente com versão/hash documentados.
- **Prazo típico:** < 1 dia. **Risco:** baixar PL defasado ou esquecer os XSDs de evento/inutilização (os golden files da Fase 3 cobrem cancelamento e contingência); nova NT publicada entre o download e a Fase 3 — reconferir versão ao iniciar a Fase 3.

## Critérios de aceite (verificáveis)

1. PFX A1 de teste em cofre; `certutil`/`openssl pkcs12` abre com a senha e mostra CNPJ do emissor piloto e validade cobrindo o horizonte das Fases 2–7 (validade restante registrada no card, com alerta se apertada) — evidência (print sem expor a senha) no card.
2. Par CSC+idCSC de **homologação** registrado no cofre, com evidência do canal de geração e do ambiente.
3. Confirmação da SEFAZ-SE de credenciamento em **homologação** (tela/protocolo/e-mail) anexada ao card; pedido de **produção** protocolado com número e data registrados (rastreável até a Fase 7).
4. XSDs: zips com versão identificada + hash SHA-256 listados no card; conjunto cobre `leiauteNFe` 4.00, evento de cancelamento, inutilização e artefatos QR v3 da NT 2025.001.
5. Nenhum arquivo sensível (PFX, senha, CSC) e nenhum XSD commitado no repositório durante o gate (`git status` limpo quanto a esses artefatos).
6. Datas de solicitação/obtenção dos 4 blocos registradas em `docs/decisions.md` §0 ou no card (insumo do critério de saída do Gate 0).

## Plano de testes / Evidências

- **Smoke do certificado (local, sem tocar o repo):** abrir o PFX com a senha e validar cadeia/vencimento (`openssl pkcs12 -info` ou `certutil -dump`); confirmar CNPJ no Subject.
- **Smoke do CSC:** apenas conferência documental do ambiente e do idCSC (6 dígitos); o teste real do hash do QR v2 acontece na Fase 3 com `GerarQrCode`.
- **Smoke do credenciamento:** consulta de status do serviço/cadastro em homologação via portal do autorizador (sem emitir nota — emissão é Fase 3).
- **Smoke dos XSDs:** validar um XML de exemplo do MOC contra o `leiauteNFe` baixado usando um validador local avulso (fora do repo) — prova que o pacote está íntegro e completo.
- Evidências: prints/protocolos anexados ao card ClickUp; hashes e versões em comentário do card.

## Dependências

- **Não depende de nenhuma tarefa do grupo B** (roda em paralelo, por desenho).
- Interno ao A4: Bloco 2 e o autosserviço do Bloco 3 tendem a exigir o certificado do Bloco 1 (acesso autenticado) — iniciar o Bloco 1 primeiro; Bloco 4 é totalmente independente.
- **Quem depende de A4:** Fase 2 (upload/validação do A1, `CscConfig`), Fase 3 (assinatura, QR v2/v3, transmissão em homologação, golden files da Fase 3 usando os XSDs), Fase 7 (credenciamento de produção concluído).

## Riscos e pontos de atenção

| Risco | Impacto | Mitigação |
|---|---|---|
| Credenciamento (homolog. e sobretudo produção) demora semanas | Fase 3 sem homologação real; Fase 7 sem go-live | **Iniciar os dois pedidos no primeiro dia do gate**; produção protocolada já em A4 |
| IE do emissor piloto irregular em SE | Credenciamento negado; em produção, Denegada (`cStat=110`) | Checar situação cadastral com o contador antes de protocolar |
| CSC de ambiente errado ou regerado sem aviso | QR Code inválido em homologação (falha difícil de diagnosticar) | Rotular ambiente no cofre; nunca regerar sem registrar; validar par CSC/idCSC na Fase 3 com vetor de teste |
| PFX/senha/CSC vazando via repo ou card | Comprometimento do material fiscal | Cofre fora do repo; evidências sempre sem segredo; regra 6 do design desde já |
| PL/NT defasados nos golden files | Golden files da Fase 3 validam contra schema errado; rejeições na SVRS | Registrar versão+hash; reconferir avisos do autorizador ao abrir a Fase 3 |
| Certificado de teste vence no meio do projeto | Homologação e smoke E2E param | Aceite exige validade cobrindo as Fases 2–7; alerta manual no card (o ciclo 30/15/7/1 dias só existe a partir da Fase 2) |
