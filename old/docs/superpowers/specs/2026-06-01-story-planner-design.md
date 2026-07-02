# Story Planner — Design Spec

**Data:** 2026-06-01
**Status:** Aprovado

## Identidade

- **Agent:** `story-planner`
- **Invocação:** `/story-planner` seguido do contexto (ideia bruta, card do Jira, história incompleta, ou texto livre)
- **Modelo:** `sonnet`
- **Tools:** `Read`, `Glob`, `Grep`
- **Skills:** `story-template`, `architecture-review`, `migration-reviewer`

## Propósito

Agent de planejamento de histórias para o time Receba. Faz o ciclo completo: escrever histórias a partir de ideias brutas, revisar histórias existentes, e analisar fatores de complexidade. Usa o repositório local para entender o contexto técnico antes de escrever critérios de aceite.

## Skill `story-template`

Skill reutilizável com:
- Template padrão de história
- Critérios de qualidade (testabilidade, ator específico, fluxos alternativos)
- Sinais de história mal escrita
- Checklist de revisão rápida

Reutilizável por outros agents no futuro (ex: pr-reviewer verificando se PR tem história associada bem escrita).

## Modos de operação

| Input recebido | Modo |
|---|---|
| Ideia/briefing cru sem estrutura | **Escritor** — cria a história do zero no template |
| História já escrita com estrutura | **Revisor** — enriquece e corrige sem impor reestruturação |
| História + pedido explícito de estimativa | **Estimador** — analisa fatores de complexidade |
| Ideia + "estime" ou "ciclo completo" | **Combinado** — escreve, revisa e estima na mesma resposta |

### Modo Escritor

1. Usa Grep/Read no repo para entender contexto técnico (ex: se a história menciona "pagamento", grep no código para entender o modelo existente)
2. Gera história no template padrão
3. Inclui notas técnicas com dependências e riscos identificados

**Template padrão:**
```
Como [ator], quero [ação], para [benefício].

Critérios de aceite:
- [ ] {critério testável 1}
- [ ] {critério testável 2}
- [ ] {critério testável n}

Notas técnicas:
- {dependências, riscos, contexto relevante}
```

### Modo Revisor

Não reescreve — aponta e corrige:
- Critérios não testáveis ("o sistema deve ser rápido" → problema)
- Ator ambíguo ("o usuário" vs "o operador de backoffice")
- Critérios faltando para fluxos alternativos (erro, permissão negada, dado inválido)
- Dependências técnicas não declaradas

### Modo Estimador

Não sugere número de pontos — lista os fatores que impactam a complexidade:
- Número de camadas tocadas (só frontend, ou frontend + backend + banco?)
- Presença de migration de banco
- Integração com sistema externo (SEFAZ, prefeitura, gateway de pagamento)
- Novidade do padrão (usa padrão existente ou precisa criar novo?)
- Cobertura de testes necessária

## Formato de saída

```
## Story Planner — [Modo detectado]

### História
Como [ator], quero [ação], para [benefício].

Critérios de aceite:
- [ ] {critério testável}

Notas técnicas:
- {contexto relevante}

### Revisão (se Modo Revisor ou Combinado)
PROBLEMA: {descrição} — {por que é um problema}
CORRIGIDO: {versão corrigida}

### Fatores de complexidade (se Modo Estimador ou Combinado)
- {fator}: {impacto — Alto / Médio / Baixo}

### Próximos passos
- [ ] {ex: validar critérios com o PO}
- [ ] {ex: quebrar em sub-tarefas se complexidade alta}
```
