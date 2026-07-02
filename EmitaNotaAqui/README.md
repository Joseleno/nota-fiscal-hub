# EmitaNotaAqui

Solution nova do produto **nota-fiscal-hub** — hub de emissão de documentos fiscais eletrônicos (NFC-e Sergipe no MVP; NFSe e NFe nas fases seguintes).

> Esta solution nasce da decisão **D-2026-07-02-01** (saída do Gate 0): reestruturar em solution nova modular em vez de evoluir o legado in-place. A implementação prévia (VisuFiscalHub) foi movida para `../old/` e serve como **referência e origem de porte cirúrgico** (Motor fiscal + testes de domínio), não como base evolutiva.

## Onde está a especificação

Toda a documentação do produto está em `../docs/`:

- **Design do produto:** `../docs/superpowers/specs/2026-07-01-nota-fiscal-hub-produto-design.md` — módulos (§2.2), fronteiras (§2.3), dependências (§2.5), testes (§4.4).
- **Plano-mestre (roadmap Gate 0 + Fases 0–7):** `../docs/superpowers/plans/2026-07-01-nota-fiscal-hub-mvp-plano-mestre.md` — inclui as *Global Constraints* que valem para todas as fases.
- **Decisões:** `../docs/decisions.md` §0 — D-2026-07-01-01..10 (ratificadas) e D-2026-07-02-01..03 (saída do Gate 0).
- **Matriz do Gate 0** (o que aproveitar/adaptar/reescrever do legado): `../docs/superpowers/reviews/2026-07-02-gate0-matriz-final.md`.
- **Specs das tarefas** (Grupos A e B já escritas): `../docs/superpowers/specs/tarefas/`.

## Estrutura-alvo (a construir na Fase 0 — tarefa B1)

Monólito modular .NET 10, 2 deployables:

```
EmitaNotaAqui/
├── EmitaNotaAqui.slnx
├── src/
│   ├── Api/                          # host HTTP
│   ├── Worker/                       # host de jobs
│   ├── BuildingBlocks/               # kernel: tenant context, outbox/inbox, idempotência, auditoria
│   └── Modules/
│       ├── ContasPlanos/{Domain,Application,Infrastructure,Contracts}
│       ├── EmpresasCertificados/{...}
│       ├── Emissao/{...}
│       ├── Motor/{...}               # porte cirúrgico de ../old/src/.../Infrastructure/Fiscal
│       └── Documentos/{...}
└── tests/
    ├── ArchitectureTests/            # NetArchTest travando fronteiras no CI (desde a Fase 0)
    └── ...
```

> O scaffold real (projetos, `.slnx`, NetArchTest) é a **tarefa B1** da Fase 0 — ver `../docs/superpowers/specs/tarefas/B1-estrutura-solution.md`. Esta pasta está intencionalmente só com este README até lá (fase atual: documentação/gate, ainda sem geração de código).

## Estado atual

Gate 0 **FECHADO** em 2026-07-02. Próximo: detalhar a Fase 0 e executar a tarefa B1 (scaffold desta solution).
