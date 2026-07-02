# Estratégia de Modelos de IA — nota-fiscal-hub

> Definida em 2026-07-02, após o fechamento das 14 specs A/B. Objetivo: manter a qualidade
> alcançada na fase de design/specs gastando menos — o "pensamento difícil" já está congelado
> nos documentos; o que resta é execução contra especificações detalhadas.

## Princípio

A qualidade daqui em diante vem de **specs com critérios de aceite verificáveis + loops de
revisão (agentes revisores, NetArchTest, testes, CI)** — não do tamanho do modelo. Modelo
grande é reservado para os momentos de **julgamento** (decisões de gate, revisões críticas),
não para execução.

## Mapa de uso

| Modelo | Custo (por 1M tokens, in/out) | Quando usar |
|---|---|---|
| **Sonnet 5** (padrão) | $3/$15 (promo $2/$10 até 31/08/2026) | Execução de tarefas bem especificadas (B1–B9), documentação, consolidação de relatórios, mecânica de ClickUp, subagentes de fan-out |
| **Opus 4.8** | $5/$25 | Gates de decisão: A1 (g0-qualidade), A2 (matriz APROVEITAR/ADAPTAR/REESCREVER), revisões adversariais de lógica fiscal crítica (SEFAZ, idempotência, tenant), debugging difícil |
| **Fable 5** | $10/$50 | Exceção — só para pivô de design/arquitetura genuinamente difícil |

Trocar de modelo na sessão: `/model` (ex.: subir para Opus antes de A1/A2 e voltar depois).

## Configuração aplicada

- **`.claude/settings.local.json`** do projeto: `"model": "sonnet"` — toda sessão nova neste
  projeto inicia no Sonnet 5. Herança: agentes e subagentes que não fixam modelo próprio
  seguem o modelo da sessão, então o ecossistema inteiro (backend-generator, revisores,
  workflows) fica mais barato de uma vez.
- **Ultracode: manter DESLIGADO no dia a dia.** É um toggle de sessão (não persiste entre
  sessões — nada a configurar em arquivo). Liga workflows multi-agente em toda tarefa
  substantiva e é o maior consumidor isolado de tokens (ex.: a revisão das 14 specs consumiu
  ~1,8M tokens de subagentes). Usar workflow só quando pedir explicitamente, ou via a
  palavra-chave `ultracode` num prompt pontual.

## Ajustes opcionais (não aplicados)

- **`effortLevel`**: o padrão do Claude Code é `xhigh`. Baixar para `high` na sessão reduz
  tokens com perda mínima em trabalho de execução; manter `xhigh` nos gates.
- **Fixar modelo por agente**: adicionar `model: opus` no frontmatter dos agentes revisores
  (pr-reviewer, quality-guardian) se quiser que revisões sempre rodem em Opus mesmo com a
  sessão em Sonnet.

## Higiene de sessão

- Parar/renovar a sessão ao atingir ~70% de contexto (regra já em memória).
- Fase atual é só-documentação: código em `src/`/`tests/` permanece só-leitura até o
  fechamento formal do Gate 0 pelo dono.
