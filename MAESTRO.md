# ambrosia Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-10-26

## Active Technologies
- TypeScript 5.x, React 18, Vite 6, Tailwind CSS 4 + shadcn/ui, i18next 25 (frontend); C# 12 / .NET 8.0 para o ajuste pontual do backend (002-frontend-web-clientes-pedidos)
- nenhum no frontend além de `localStorage` (chave `login-with-metamask:auth` com `{ token, expiresAt, user }`); dados vêm da API (PostgreSQL via HU 001) (002-frontend-web-clientes-pedidos)

- C# 12 / .NET 8.0 (LTS) (001-api-cadastro-clientes-pedidos)

## Project Structure

```text
backend/
frontend/
tests/
```

## Commands

# Frontend (Node 22+) — tudo a partir de `frontend/`

```powershell
cd frontend
npm ci
npm run dev          # http://localhost:5173 (proxy /api → localhost:5000)
npm test             # Vitest + RTL
npm run lint
npm run type-check
npm run build -- --mode production
npm run verify:prod  # prova da SC-008: falha se dev-login sobreviver no dist/
```

# Backend (.NET 8)

```powershell
cd backend
dotnet test Ambrosia.Tests --filter OrderRepositoryTest
```

# Docker (sem Docker Desktop: Engine no WSL2 — skill `wsl-docker`)

```powershell
wsl --cd /mnt/c/repos/ambrosia -e docker compose --profile frontend up -d --build
```

## Code Style

C# 12 / .NET 8.0 (LTS): Follow standard conventions

TypeScript 5 / React 18: componentes PascalCase, funções e variáveis camelCase em
arrow functions, constantes UPPER_CASE, sempre `interface` (nunca `type`), nenhum
`any`. Pastas com caixa fixa pela constituição §III: `src/Contexts/` e
`src/Services/` maiúsculos, `src/hooks/` e `src/types/` minúsculos — o build roda
em Linux no Docker e caixa importa. Toda string visível vem de
`frontend/src/i18n/locales/pt-BR.ts`.

## Recent Changes
- 002-frontend-web-clientes-pedidos: Added TypeScript 5.x (frontend); C# 12 / .NET 8.0 para o ajuste pontual do backend

- 001-api-cadastro-clientes-pedidos: Added C# 12 / .NET 8.0 (LTS)

<!-- MANUAL ADDITIONS START -->

## Idioma

- Responda sempre em português do Brasil, com acentuação correta, mesmo que o pedido, os templates ou os arquivos estejam em outro idioma.
- Os artefatos (spec.md, plan.md, tasks.md, decisions.md), as mensagens de commit e os comentários de código também vão em português.
- Identificadores de código, nomes de arquivos, comandos e termos técnicos ficam no original.

<!-- MANUAL ADDITIONS END -->
