# ambrosio Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-10-27

## Active Technologies
- TypeScript 5.x, React 18, Vite 6, Tailwind CSS 4 + shadcn/ui, i18next 25 (frontend); C# 12 / .NET 8.0 para o ajuste pontual do backend (002-frontend-web-clientes-pedidos)
- nenhum no frontend além de `localStorage` (chave `login-with-metamask:auth` com `{ token, expiresAt, user }`); dados vêm da API (PostgreSQL via HU 001) (002-frontend-web-clientes-pedidos)
- C# 12 / .NET 8.0 (backend); TypeScript 5.x / React 18 (frontend) (003-email-password-jwt-auth)
- PostgreSQL (tabela `users`, coluna `password_hash varchar(260)` já existente; sem migração). Frontend: `localStorage` na chave `login-with-metamask:auth` com `{ token, expiresAt, user }` (inalterado) (003-email-password-jwt-auth)
- C# 12 / .NET 8.0 (backend); TypeScript 5.x / React 18 + Vite 6 (frontend) (006-mais-vendas-import)
- PostgreSQL (migração `AddMaisVendasImport`: `import_runs`, `import_run_blocks`, `import_run_changes`, `mais_vendas_accounts`; colunas novas em `orders`/`order_items`); fotos no storage S3 da 004 (`ImageSource.Bot`); `Microsoft.Playwright` + Chromium na imagem da API; nenhuma credencial do +Vendas persistida (006-mais-vendas-import)

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
npm run verify:prod  # prova do pacote: falha se dev-login/DevLoginForm/VITE_DEV_LOGIN sobreviver no dist/ (qualquer modo)
```

# Backend (.NET 8)

```powershell
cd backend
dotnet test Ambrosio.Tests --filter OrderRepositoryTest
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
- 006-mais-vendas-import: Added C# 12 / .NET 8.0 (backend); TypeScript 5.x / React 18 + Vite 6 (frontend)
- 003-email-password-jwt-auth: Added C# 12 / .NET 8.0 (backend); TypeScript 5.x / React 18 (frontend)
- 002-frontend-web-clientes-pedidos: Added TypeScript 5.x (frontend); C# 12 / .NET 8.0 para o ajuste pontual do backend


<!-- MANUAL ADDITIONS START -->

## Idioma

- Responda sempre em português do Brasil, com acentuação correta, mesmo que o pedido, os templates ou os arquivos estejam em outro idioma.
- Os artefatos (spec.md, plan.md, tasks.md, decisions.md), as mensagens de commit e os comentários de código também vão em português.
- Identificadores de código, nomes de arquivos, comandos e termos técnicos ficam no original.

<!-- MANUAL ADDITIONS END -->
