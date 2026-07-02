# Frontend Agents — Design Spec

**Data:** 2026-06-01
**Status:** Aprovado

## Identidade

### Agent Angular
- **Nome:** `frontend-angular`
- **Invocação:** `/frontend-angular` seguido da descrição do componente/feature/page
- **Modelo:** `sonnet`
- **Tools:** `Bash`, `Read`, `Glob`, `Grep`, `Edit`, `Write`
- **Skills:** `review-angular`

### Agent React
- **Nome:** `frontend-react`
- **Invocação:** `/frontend-react` seguido da descrição do componente/feature/page
- **Modelo:** `sonnet`
- **Tools:** `Bash`, `Read`, `Glob`, `Grep`, `Edit`, `Write`
- **Skills:** `review-react`

### Skills
- **`review-angular`** — padrões Angular 21/TypeScript, especializa e complementa `review-frontend`
- **`review-react`** — padrões React/TypeScript, especializa e complementa `review-frontend`

Ambas as skills são reutilizáveis pelo `pr-reviewer` e `bug-hunter`.

## Modos de operação (ambos os agents)

| Input recebido | Modo |
|---|---|
| Descrição de componente/feature sem código | **Gerador** — cria o código do zero |
| Código existente + pedido de melhoria/correção | **Melhorador** — refatora preservando lógica |
| Código existente sem pedido explícito | **Revisor** — aplica skill de review e reporta |
| Descrição + "ciclo completo" | **Combinado** — gera + auto-revisa + entrega relatório |

### Modo Gerador

1. Lê o repositório para entender padrões existentes (estrutura de pastas, convenções de nomes, imports)
2. Gera o código seguindo os padrões encontrados
3. Auto-revisa com a skill correspondente
4. Corrige issues encontrados na auto-revisão
5. Entrega código final + relatório de auto-revisão

### Modo Melhorador

1. Lê o arquivo existente com `Read`
2. Identifica o que mudar sem alterar comportamento externo
3. Aplica as mudanças com `Edit`
4. Auto-revisa com a skill
5. Entrega diff descrito + relatório

### Modo Revisor

Aplica a skill de review e entrega relatório CRÍTICO / WARNING / SUGESTÃO com arquivo:linha — mesmo formato do `pr-reviewer`.

## Skill `review-angular` (Angular 21 / TypeScript)

Checklist em 5 eixos:

### 1. Componentes
- Standalone components (sem NgModule desnecessário)
- `OnPush` change detection obrigatório em componentes de lista/tabela
- Inputs/Outputs com tipos explícitos (sem `any`)
- Lifecycle hooks usados corretamente (`ngOnDestroy` com unsubscribe)

### 2. Signals e reatividade
- Preferir `signal()` e `computed()` sobre `BehaviorSubject` para estado local
- `effect()` só para side effects, não para derivar estado
- `toSignal()` para converter Observables em Signals na borda do template

### 3. Templates
- Sem lógica complexa no template (extrair para computed/método)
- `@if` / `@for` / `@switch` (nova sintaxe Angular 17+, não `*ngIf`/`*ngFor`)
- TrackBy obrigatório em `@for`

### 4. Services e injeção
- `inject()` no lugar de constructor injection
- Services com `providedIn: 'root'` salvo quando escopo menor é necessário
- HTTP calls via `HttpClient` com tipagem explícita

### 5. Performance
- Lazy loading de rotas
- `defer` blocks para componentes pesados
- Sem subscriptions não fechadas

## Skill `review-react` (React / TypeScript)

Checklist em 5 eixos:

### 1. Componentes
- Componentes funcionais com TypeScript (interface explícita de props, sem `any`)
- Componentes pequenos e focados (uma responsabilidade)

### 2. Hooks
- `useCallback` e `useMemo` onde há risco real de re-render (não preventivamente)
- `useEffect` com dependencies array correto — sem dependências faltando
- Custom hooks para lógica reutilizável

### 3. Estado
- Estado local com `useState`, estado global com Context ou biblioteca (Zustand, Redux)
- Sem prop drilling além de 2 níveis
- Estado derivado como variável, não como `useState`

### 4. Performance
- `React.memo` em componentes que recebem as mesmas props frequentemente
- Lazy loading com `React.lazy` + `Suspense` para rotas/componentes pesados
- Listas com `key` estável (não índice do array)

### 5. TypeScript
- Sem `as any` ou `@ts-ignore` sem comentário justificando
- Tipos de retorno explícitos em funções complexas
- Generics onde aplicável em vez de duplicação de tipo

## Formato de saída (Modo Revisor / auto-revisão)

```
## Frontend Review — [Angular | React]

### CRÍTICO (bloqueia merge)
{Se nenhum: "Nenhum issue crítico encontrado."}

[Arquivo:Linha] — {Título}
Problema: {descrição}
Solução: {código ou configuração corrigida}

### WARNING
{mesmo formato}

### SUGESTÃO
{mesmo formato}

### Score
Qualidade geral: X/10 — {resumo em uma linha}
```
