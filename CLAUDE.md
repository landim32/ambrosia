# ambrosio Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-10-29

## Active Technologies
- C# 12 / .NET 8.0 (backend); TypeScript 5.x / React 18 + Vite 6 (frontend) (006-mais-vendas-import)
- PostgreSQL (migração `AddMaisVendasImport`: `import_runs`, `import_run_blocks`, `import_run_changes`, `mais_vendas_accounts`; colunas novas em `orders`/`order_items`); fotos no storage S3 da 004 (`ImageSource.Bot`); `Microsoft.Playwright` + Chromium na imagem da API; nenhuma credencial do +Vendas persistida (006-mais-vendas-import)
- C# 12 / .NET 8.0 (backend, novo host `Ambrosio.McpServer`); TypeScript 5.x / React 18 (frontend: só a tela de importação lê `inicio` e `fim` da URL) + `ModelContextProtocol.AspNetCore` 2.x (versão fixada na implementação), OpenIddict 7.7.x (`OpenIddict.AspNetCore`, `OpenIddict.EntityFrameworkCore`), ASP.NET Core Razor Pages (telas do AS), `Microsoft.AspNetCore.Authentication.JwtBearer` 8.x (validação no MCP), FluentValidation e serviços existentes das features 001, 003, 004 e 006 (007-chatgpt-mcp-plugin)
- PostgreSQL, mesmo `AmbrosioContext`. Migração `AddOAuthServer`: `oauth_applications`, `oauth_authorizations`, `oauth_scopes`, `oauth_tokens` e `mcp_audit_entries` (007-chatgpt-mcp-plugin)
- C# 12 / .NET 8.0 (backend: API, host do MCP, worker); TypeScript 5.x / React 18 + Vite 6 (frontend) + as existentes (EF Core 9 + Npgsql, FluentValidation, OpenIddict/ModelContextProtocol no host do MCP, Playwright no coletor); `System.Security.Cryptography.AesGcm` (BCL) para a cifra. Nenhum pacote novo (008-import-credentials)
- PostgreSQL, `AmbrosioContext`. Migração `AddMaisVendasCredentials`: 5 colunas em `mais_vendas_accounts` e 2 em `import_runs` (008-import-credentials)
- C# 12 / .NET 8.0 (backend: API e host do MCP); TypeScript 5.x / React 18 + Vite 6 (frontend) + as existentes (EF Core 9 + Npgsql, FluentValidation, AWSSDK.S3, ModelContextProtocol 2.3, OpenIddict). Novas: `SixLabors.ImageSharp` 3.1.x (só `Ambrosio.Infra` e `Ambrosio.ApiTests`), `react-easy-crop` 5.x e `@radix-ui/react-tabs` 1.x (009-customer-photos)
- PostgreSQL, `AmbrosioContext`. Migração `AddCustomerPhotos`: tabela `customer_photos` e 5 colunas de foto de perfil em `customers`; original, miniatura e recorte são `stored_images` da 004 (009-customer-photos)

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
- 009-customer-photos: Added C# 12 / .NET 8.0 (backend: API e host do MCP); TypeScript 5.x / React 18 + Vite 6 (frontend) + as existentes (EF Core 9 + Npgsql, FluentValidation, AWSSDK.S3, ModelContextProtocol 2.3, OpenIddict). Novas: `SixLabors.ImageSharp` 3.1.x, `react-easy-crop` 5.x, `@radix-ui/react-tabs` 1.x
- 008-import-credentials: Added C# 12 / .NET 8.0 (backend: API, host do MCP, worker); TypeScript 5.x / React 18 + Vite 6 (frontend) + as existentes (EF Core 9 + Npgsql, FluentValidation, OpenIddict/ModelContextProtocol no host do MCP, Playwright no coletor); `System.Security.Cryptography.AesGcm` (BCL) para a cifra. Nenhum pacote novo
- 007-chatgpt-mcp-plugin: Added C# 12 / .NET 8.0 (backend, novo host `Ambrosio.McpServer`); TypeScript 5.x / React 18 (frontend: só a tela de importação lê `inicio` e `fim` da URL) + `ModelContextProtocol.AspNetCore` 2.x (versão fixada na implementação), OpenIddict 7.7.x (`OpenIddict.AspNetCore`, `OpenIddict.EntityFrameworkCore`), ASP.NET Core Razor Pages (telas do AS), `Microsoft.AspNetCore.Authentication.JwtBearer` 8.x (validação no MCP), FluentValidation e serviços existentes das features 001, 003, 004 e 006


<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
