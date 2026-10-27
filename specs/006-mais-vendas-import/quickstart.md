# Quickstart: validar a importação do +Vendas

**Feature**: `006-mais-vendas-import` | Contratos: [contracts/imports-api.md](./contracts/imports-api.md) | Modelo: [data-model.md](./data-model.md)

Este guia prova a feature de ponta a ponta na **homologação** (Docker no WSL, coletor `Fake` — research §3) e indica como ligar o coletor real em Development. Não contém código de implementação.

## 1. Pré-requisitos

- Docker Engine no WSL2 (skill `wsl-docker`), `.env` preenchido a partir de `.env.example` (inclui `MAIS_VENDAS_COLLECTOR=Fake` para homolog).
- .NET 8 SDK e Node 22+ para a suíte de API e os testes unitários do frontend.

## 2. Subir a homologação com o código da feature

```powershell
wsl --cd /mnt/c/repos/ambrosia -e docker compose --profile frontend up -d --build
wsl --cd /mnt/c/repos/ambrosia -e docker compose logs migrator --tail 20   # migração AddMaisVendasImport aplicada
```

Esperado: `http://localhost:8080/api/swagger` lista `/imports/mais-vendas/*` com `password` em `format: password`; `http://localhost:8080/importacao` redireciona ao login sem sessão (FR-001).

## 3. Testes de API (constituição §VII)

```powershell
cd backend
dotnet test Ambrosia.ApiTests --filter "FullyQualifiedName~ImportsController|FullyQualifiedName~UnauthorizedAccess|FullyQualifiedName~Isolation"
```

Esperado: todos verdes. A suíte cria seus usuários por cadastro + login e usa os cenários do coletor fake (research §3/§16). Depois, a suíte completa: `dotnet test Ambrosia.ApiTests`.

## 4. Cenários manuais na tela (`http://localhost:8080`)

Crie uma conta em `/cadastro`, entre e abra **Importar** no cabeçalho. Logins do fake: `conta+cenario`; senha = versão do conteúdo (`v1`, `v2`) ou `recusada`.

| # | Passos | Esperado |
|---|---|---|
| 1 | Abrir `/importacao` com conta nova | data inicial vazia e obrigatória; data final = hoje; login/senha vazios (US1-2) |
| 2 | `s482+ok` / `v1`, 26/09/2026 → 25/10/2026, iniciar | confirmação em ≤ 3 s; 5 blocos; progresso atualiza sozinho (≤ 5 s); relatório `succeeded` com 49 pedidos e total líquido R$ 123.300,58 em `/pedidos` (SC-001); campo de senha limpo (FR-007) |
| 3 | Repetir o passo 2 com `v1` | 0 inseridos, 0 atualizados, 100 % inalterados (SC-002); nenhuma linha nova em `orders`, `order_items`, `customers`, `stored_images` |
| 4 | Repetir com `v2` | alguns pedidos `updated` com `changedFields` (ex.: `total,status`); nome de um cliente atualizado; demais dados do cliente intactos (US3-5, US5-3) |
| 5 | Editar à mão o total de um pedido importado em `/pedidos/{id}` e reimportar com `v2` | valor volta ao da origem; pedido `updated` com `total` listado (US3-8) |
| 6 | `s482+falha-bloco3` / `v1` | execução `failed`; blocos 1–2 `succeeded`, bloco 3 `failed` com "14 pedidos gravados", blocos 4–5 `notExecuted`; botão **Repetir este bloco** abre o formulário com as datas do bloco 3 (US3-1/2) |
| 7 | Repetir o bloco 3 e depois o período inteiro com `s482+ok` / `v1` | 14 inalterados + restantes inseridos; estado final idêntico ao do passo 2, sem duplicatas (SC-003) |
| 8 | `s482+contador-divergente` | bloco falha **sem** nenhum pedido gravado dele (SC-009) |
| 9 | Marcar **Simular** com `s482+ok` em conta nova | relatório identificado como simulação com o que *seria* inserido; contagens de pedidos/clientes/imagens iguais antes e depois (SC-011) |
| 10 | `s482+foto-erro` e `s482+foto-gif` | pedidos gravados; itens sem imagem listados como "imagem com erro"; SKU repetido → 1 objeto no storage (SC-010) |
| 11 | Iniciar `s482+ok` e, em outra aba, iniciar de novo | segunda recusada com "Já existe importação em andamento" (SC-008); fechar e reabrir a aba mostra o progresso (US7-1) |
| 12 | Cancelar durante a execução | termina ao fim do bloco atual como `cancelled`, blocos concluídos preservados (US7-4) |
| 13 | `s482+ok` / `recusada` três vezes | 3ª recusa → novos inícios bloqueados por 15 min com horário informado; `GET /defaults` traz `lockedUntil` (FR-008a) |
| 14 | Após vínculo com `s482`, importar com `s999+ok` | `failed` com "Conta do +Vendas diferente da vinculada", nada gravado (US2-6) |
| 15 | `s482+2fa` | `failed` com mensagem de etapa não suportada (FR-046) |
| 16 | Período 26/09 → 25/10 após o passo 7, cenário `s482+vazio` em simulação | aviso de queda de contagem na tela (FR-051) |

## 5. Prova de que as credenciais não ficam (SC-004)

Use valores identificáveis, por exemplo login `s482+ok` e senha `SENHA-TESTE-XYZ123`, execute uma importação e procure:

```powershell
# Banco (todas as tabelas)
wsl -e docker exec ambrosia-homolog-db pg_dump -U $env:POSTGRES_USER $env:POSTGRES_DB | Select-String -Pattern "SENHA-TESTE-XYZ123|s482\+ok"
# Logs da API
wsl --cd /mnt/c/repos/ambrosia -e docker compose logs api | Select-String -Pattern "SENHA-TESTE-XYZ123|s482\+ok"
```

Esperado: nenhuma ocorrência. No navegador (DevTools → Application): `localStorage`, `sessionStorage` e cookies sem a senha; o campo tem `autocomplete="off"`. Nos corpos de resposta de todas as rotas de importação (Swagger ou Bruno `bruno/Imports/*.bru`): nenhuma ocorrência (o teste de API `Responses_NeverContainCredentials` automatiza esta parte). CPF: `Select-String -Pattern "\d{3}\.?\d{3}\.?\d{3}-?\d{2}"` nos logs só encontra máscaras `***.456.789-**`.

## 6. Reinicialização no meio da execução (FR-020)

```powershell
wsl --cd /mnt/c/repos/ambrosia -e docker compose restart api
```

Esperado: ao voltar à tela, a execução aparece `failed` com "Execução interrompida pela reinicialização do servidor."; repetir o período é seguro (tudo `unchanged`/`inserted`, sem duplicatas).

## 7. Coletor real em Development (opcional, com conta do +Vendas)

```powershell
cd backend
# appsettings.Development.json: "MaisVendas": { "Collector": "Playwright" }
dotnet build Ambrosia.API
pwsh Ambrosia.API/bin/Debug/net8.0/playwright.ps1 install chromium
dotnet run --project Ambrosia.API
```

Primeira execução **sempre em Simular** (assumptions da spec): confere contadores/totais, o formato da resposta `client-data` e os seletores. Verifique no log as rotas visitadas (só `/login*`, `/cotaPiloto*`, `/storeOmni*`, `/detailOrder/*`) e o ritmo ≤ 2 req/s (SC-012, FR-043). Termos de uso da Somalabs conferidos antes de liberar.

## 8. Frontend (constituição §VIII: só testes unitários)

```powershell
cd frontend
npm run lint; npm run type-check; npm test
```

Esperado: testes de `ImportForm`, `ImportProgress`, `ImportReport`, `importForm` e `ImportContext` verdes; nenhum arquivo Playwright/E2E.

## 9. Encerramento do ciclo

Homologação reconstruída com o código final + `dotnet test Ambrosia.ApiTests` inteiro verde contra `http://localhost:8080/api` (constituição §VII). Para produção: verificar `shm_size`/memória do serviço `api`, `MaisVendas__Collector=Playwright` e o checklist em `deploy/README.md`.
