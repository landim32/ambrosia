# ambrosio Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-10-27

## Active Technologies
- C# 12 / .NET 8.0 (backend); TypeScript 5.x / React 18 + Vite 6 (frontend) (006-mais-vendas-import)
- PostgreSQL (migração `AddMaisVendasImport`: `import_runs`, `import_run_blocks`, `import_run_changes`, `mais_vendas_accounts`; colunas novas em `orders`/`order_items`); fotos no storage S3 da 004 (`ImageSource.Bot`); `Microsoft.Playwright` + Chromium na imagem da API; nenhuma credencial do +Vendas persistida (006-mais-vendas-import)

- C# 12 / .NET 8.0 (backend); nginx 1.27 (frontend estático, só CSP muda) (004-s3-image-storage)

## Project Structure

```text
backend/
frontend/
tests/
```

## Commands

# Add commands for C# 12 / .NET 8.0 (backend); nginx 1.27 (frontend estático, só CSP muda)

## Code Style

C# 12 / .NET 8.0 (backend); nginx 1.27 (frontend estático, só CSP muda): Follow standard conventions

## Recent Changes
- 006-mais-vendas-import: Added C# 12 / .NET 8.0 (backend); TypeScript 5.x / React 18 + Vite 6 (frontend)

- 004-s3-image-storage: Added C# 12 / .NET 8.0 (backend); nginx 1.27 (frontend estático, só CSP muda)

<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
