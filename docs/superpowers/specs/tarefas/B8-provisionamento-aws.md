# Spec B8 — Provisionamento AWS mínimo (trilha paralela)
> Card: https://app.clickup.com/t/86e24c1kf | Grupo B | Gerada em 2026-07-02 por time de agentes (escritor especialista + revisor adversarial)

## Objetivo
Provisionar, como trilha paralela da Fase 0, a infraestrutura AWS mínima de **não-produção** que desbloqueia entregáveis das Fases 2 e 4: chave KMS para envelope encryption de certificados A1/CSC (consumidor: Fase 2/D1), bucket S3 com versionamento + Object Lock para guarda de XML/DANFE (consumidor: Fase 4/F1) e RDS PostgreSQL de não-produção. Critério-mestre: recursos acessíveis pela aplicação em dev/homolog com credenciais seguras (nenhum segredo em repositório).

## Contexto e referências
- Design §2.2 (custódia com KMS, DEK por empresa), §2.2 Documentos (SSE-KMS, versionamento, Object Lock, retenção ≥ 5 anos, separado por ambiente), §4.5 Deploy: `docs/superpowers/specs/2026-07-01-nota-fiscal-hub-produto-design.md`.
- Plano-mestre, Fase 0 (linha "Provisionamento AWS mínimo… trilha paralela") e Global Constraints (TDD em todas as fases; logs sem PII; `tpAmb` permeia guarda): `docs/superpowers/plans/2026-07-01-nota-fiscal-hub-mvp-plano-mestre.md`.
- Decisões D-2026-07-01-07 (um ambiente produtivo do hub; "homologação" = empresas `tpAmb=2`) e D-2026-07-01-08 (destruição auditada de A1/CSC): `docs/decisions.md` §0.
- Handoff Gate 0: `docs/superpowers/handoffs/2026-07-01-revisao-docs-e-gate0-handoff.md`.

**Assunções dependentes da matriz do Gate 0 (A2) — validar antes de aplicar `terraform apply`:**
1. Prefixo de nomes `nfh-` assume solution nova (matriz preliminar aponta reestruturação); se o Gate 0 decidir evoluir o repo legado, só muda o prefixo — a topologia desta spec permanece.
2. Conta AWS única com segregação por sufixo de ambiente (`-nonprod`) e tags; contas separadas por ambiente ficam adiadas para a Fase 7/I2 (registrar como decisão).
3. Região `sa-east-1` (dados fiscais no Brasil, latência a SEFAZ-SE) — registrar como decisão; custo ~30–50% acima de `us-east-1` já refletido na estimativa.

## Escopo (dentro / fora)
**Dentro (somente não-produção):**
- IaC do zero em **Terraform** (proposta desta spec; registrar em `docs/decisions.md` §0 como `D-<data>-terraform-iac`, junto com região e conta única), em `infra/terraform/` com backend de estado S3 + lock nativo (`use_lockfile`).
- **KMS:** 1 CMK simétrica `nfh-nonprod-certificados` (envelope encryption — a aplicação chama `GenerateDataKey`/`Decrypt` com `EncryptionContext`), 1 CMK `nfh-nonprod-documentos` (SSE-KMS do bucket). Rotação automática anual habilitada; key policy restringe uso ao principal da aplicação; `kms:ScheduleKeyDeletion`/`PutKeyPolicy` só para o principal de administração.
- **S3:** bucket `nfh-nonprod-documentos-<account_id>` com Object Lock habilitado na criação (irreversível depois), versionamento, SSE-KMS obrigatório (bucket policy nega `PutObject` sem `aws:kms`), Block Public Access total, TLS-only. **Retenção default em não-produção: modo `GOVERNANCE`, 1 dia** — suficiente para testar o comportamento de lock sem prender lixo de teste; o modo de produção (`COMPLIANCE` × `GOVERNANCE`, 5 anos) é **decisão em aberto a registrar antes da Fase 7/I2**. Lifecycle: expirar versões não-correntes em 30 dias (não-produção dispensa guarda de 5 anos — design §3.7).
- **RDS PostgreSQL:** `nfh-nonprod-pg`, `db.t4g.micro`, Single-AZ, gp3 20 GB (autoscaling até 50 GB), PostgreSQL ≥ 16, `storage_encrypted` (chave AWS-managed basta em não-prod), backup 7 dias, `deletion_protection = true`, `publicly_accessible = false` — acesso do time via túnel SSM (instância/endpoint) ou VPN existente; senha master gerenciada pelo RDS (`manage_master_user_password` → Secrets Manager).
- **IAM de menor privilégio:** role `nfh-nonprod-app` (futura task role ECS; hoje assumível pelo principal de CI e por devs via `sts:AssumeRole`) com política inline mínima: `kms:GenerateDataKey`/`kms:Decrypt` **apenas** nos dois ARNs de CMK e com condição de `EncryptionContext` presente; `s3:PutObject`/`GetObject`/`GetObjectVersion`/`ListBucket` apenas no bucket/prefixos; `secretsmanager:GetSecretValue` apenas no segredo do RDS. **Sem** `s3:DeleteObject`, `s3:BypassGovernanceRetention`, `kms:*` administrativo. Humanos via IAM Identity Center/SSO (sem access keys de longa duração); em CI, OIDC do provedor → `AssumeRole` (nenhuma credencial estática).
- **Tagging obrigatório** via `default_tags` do provider: `Project=nota-fiscal-hub`, `Environment=nonprod`, `ManagedBy=terraform`, `CostCenter=nfh-mvp`, `Owner=<dono>`, `Consumer=fase-2|fase-4|fase-0` por recurso.
- **AWS Budgets:** alerta em US$ 50/mês (80% e 100%) para a tag `Project`.

**Fora (explícito — NÃO provisionar agora):**
- Tudo de produção = Fase 7/I2: RDS Multi-AZ/PITR dimensionado, bucket de produção com retenção legal de 5 anos e modo definitivo, CMKs de produção, ECS/Fargate, ALB, VPC endpoints, WAF, CloudFront, alarmes/dashboards (§4.3), DNS/certificados TLS públicos.
- RabbitMQ/SQS (outbox é in-process no MVP), ElastiCache, réplicas de leitura, contas AWS multi-ambiente.

## Abordagem / Passos
Ordem TDD (constraint global): **testes primeiro em cada passo** — testes nativos do Terraform (`.tftest.hcl`, rodam contra `plan`, sem custo) e smoke xUnit contra os recursos reais depois do apply.

1. **Bootstrap:** criar `infra/terraform/{main.tf,variables.tf,outputs.tf,tests/}`; backend S3 de estado (`nfh-terraform-state-<account_id>`, versionado, criado uma única vez fora do estado); pin de versões (Terraform ≥ 1.10, provider aws ~> 5.x).
2. **Testes de plano primeiro:** escrever `tests/nonprod.tftest.hcl` com `command = plan` afirmando: Object Lock habilitado + versionamento + SSE-KMS + Block Public Access no bucket; `enable_key_rotation = true` nas 2 CMKs; `publicly_accessible = false`, `deletion_protection = true`, `storage_encrypted = true` no RDS; presença das tags obrigatórias. Rodar: falham (recursos inexistentes) → escrever os módulos → verde.
3. **Módulos:** `kms.tf`, `s3.tf`, `rds.tf` (+ `vpc.tf` mínimo: VPC default ou VPC dedicada /24 com 2 subnets privadas — dedicada preferida; decidir no PR), `iam.tf`, `budgets.tf`.
4. **Contrato de configuração para a aplicação** (consumido nas Fases 2/4; opções tipadas no kernel):
   ```csharp
   public sealed class AwsRecursosOptions
   {
       public required string Region { get; init; }                 // "sa-east-1"
       public required string KmsCertificadosKeyArn { get; init; }  // Fase 2/D1 — GenerateDataKey/Decrypt
       public required string S3DocumentosBucket { get; init; }     // Fase 4/F1 — SSE-KMS + Object Lock
   }
   // Uso esperado (Fase 2): GenerateDataKeyRequest { KeyId=KmsCertificadosKeyArn, KeySpec=AES_256,
   //   EncryptionContext = { ["empresaId"]=..., ["contaId"]=... } }  — contexto obrigatório na key policy.
   ```
   Os ARNs saem de `terraform output` → variáveis de ambiente/user-secrets (nunca commitados).
5. **Smoke xUnit** (`tests/.../AwsSmokeTests.cs`, `[Trait("Category","AwsSmoke")]`, excluído do CI default; roda com perfil AssumeRole): ver Plano de testes.
6. **Registrar decisões** em `docs/decisions.md` §0: Terraform como IaC, região, conta única nonprod, retenção GOVERNANCE/1 dia em não-prod + pendência do modo de produção.
7. **Runbook curto** em `infra/terraform/README.md`: como assumir a role, aplicar, rotacionar credenciais, destruir com segurança (ordem: esvaziar versões fora de lock → bucket).

## Critérios de aceite (verificáveis)
1. `terraform validate && terraform test` verdes em `infra/terraform/` (CI pode rodar ambos sem credenciais de apply).
2. `aws s3api get-object-lock-configuration --bucket nfh-nonprod-documentos-<id>` → `ObjectLockEnabled=Enabled`, modo `GOVERNANCE`, 1 dia; `get-bucket-versioning` → `Enabled`; `get-public-access-block` → 4× `true`.
3. `aws kms describe-key` nas 2 CMKs → `Enabled=true`; `get-key-rotation-status` → `true`.
4. `aws rds describe-db-instances --db-instance-identifier nfh-nonprod-pg` → `PubliclyAccessible=false`, `DeletionProtection=true`, `StorageEncrypted=true`.
5. `dotnet test --filter Category=AwsSmoke` verde com credenciais da role `nfh-nonprod-app` (prova o critério "acessível pela aplicação com credenciais seguras").
6. Menor privilégio provado: `aws iam simulate-principal-policy` para `nfh-nonprod-app` retorna `implicitDeny` para `kms:ScheduleKeyDeletion`, `s3:DeleteBucket` e `s3:BypassGovernanceRetention`.
7. Nenhum segredo/ARN sensível no git (`git grep -iE 'AKIA|aws_secret|BEGIN.*PRIVATE'` vazio); tags obrigatórias presentes em 100% dos recursos (`aws resourcegroupstaggingapi get-resources --tag-filters Key=Project`).
8. Decisões do passo 6 registradas em `docs/decisions.md` §0.

## Plano de testes / Evidências
- **Terraform test (pré-apply, TDD):** asserts do passo 2; evidência = saída do `terraform test` anexada ao PR.
- **Smoke xUnit (pós-apply):** (a) KMS: `GenerateDataKey` com `EncryptionContext` → `Decrypt` devolve a mesma DEK; `Decrypt` **sem** o contexto → `InvalidCiphertextException` (prova o contexto obrigatório); (b) S3: `PutObject` (herda SSE-KMS + lock) → `DeleteObjectVersion` da versão travada → `AccessDenied` (Object Lock efetivo); `PutObject` pedindo `SSE=AES256` → negado pela bucket policy; (c) RDS: `SELECT 1` via Npgsql com senha lida do Secrets Manager pela role (via túnel SSM).
- **Custo:** screenshot/`aws budgets describe-budgets` do budget de US$ 50 ativo.
- **Estimativa mensal não-produção (sa-east-1):** RDS db.t4g.micro Single-AZ ≈ US$ 19 + gp3 20 GB ≈ US$ 4 + backup ≈ US$ 1; KMS 2 CMKs = US$ 2 (+ requests desprezível); S3 < 5 GB + versões ≈ US$ 1; Secrets Manager 1 segredo ≈ US$ 0,40; estado TF + budget ≈ US$ 0. **Total ≈ US$ 27–35/mês** (teto do budget: 50).

## Dependências
- **A2 (matriz do Gate 0):** só afeta prefixo de nomes e localização do código de opções (assunções acima); o apply pode ocorrer em paralelo ao Gate 0, o merge do contrato C# não.
- Conta AWS com billing ativo + acesso de administração para o bootstrap (uma vez); IAM Identity Center configurado para humanos.
- **Desbloqueia:** Fase 2/D1 (custódia KMS) e Fase 4/F1 (guarda S3); Fase 0/CI de integração usa Testcontainers, **não** este RDS — sem dependência inversa.

## Riscos e pontos de atenção
- **Object Lock é irreversível no bucket** — errar o nome/modo exige recriar o bucket; por isso GOVERNANCE/1 dia em não-prod e o modo de produção adiado como decisão explícita (Fase 7/I2).
- **GOVERNANCE ≠ COMPLIANCE:** GOVERNANCE é contornável com `s3:BypassGovernanceRetention` — aceitável em não-prod (a role da app não tem a permissão), inaceitável sem análise para a guarda legal de 5 anos de produção.
- **Estado do Terraform contém dados sensíveis** (endpoint, ARNs): bucket de estado versionado, cifrado, acesso restrito à role de CI e admins.
- **RDS não-público + time remoto:** o túnel SSM adiciona atrito; se virar gargalo, a alternativa (SG com allowlist de IPs fixos e `publicly_accessible=true`) deve ser decisão registrada, nunca default silencioso.
- **Deriva manual (console)**: qualquer mudança fora do Terraform quebra o `plan`; regra do time: console é somente-leitura em recursos `ManagedBy=terraform`.
- **Custo silencioso:** versões S3 acumulando em testes → lifecycle de 30 dias já mitiga; budget de US$ 50 alerta antes de surpresa.
- **Credenciais em dev local:** proibido access key estática no `.env` do repo; usar `aws sso login` + AssumeRole; smoke tests devem falhar com mensagem clara quando não houver credencial (skip explícito, não verde falso).
