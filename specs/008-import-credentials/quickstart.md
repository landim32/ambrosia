# Quickstart: credenciais do +Vendas, importação pelo plugin e "Atualizar base" (008)

## 1. Configuração

Gere a chave das credenciais (base64 de 32 bytes) e coloque no ambiente:

```bash
openssl rand -base64 32
```

| Ambiente | Onde |
|---|---|
| Development | `MaisVendas:CredentialsKey` no `appsettings.Development.json` (valor local, fora de produção) |
| Homolog | `AMBROSIO_MAIS_VENDAS_CREDENTIALS_KEY` no `.env` (serviços `api` e `mcp`) |
| Production | GitHub Secret `AMBROSIO_MAIS_VENDAS_CREDENTIALS_KEY`, gravado pelo pipeline no `.env.prod` |

Sem a chave, a API e o host do MCP não sobem. Trocar a chave invalida as credenciais já gravadas (a vendedora cadastra de novo).

Migração: `AddMaisVendasCredentials` (aplicada pelo `migrator`).

## 2. Verificação manual (tela)

1. Entrar no Ambrosio e abrir **Importação**: sem credenciais, aparece o aviso com **Ir para a configuração**.
2. Em **Configurações › +Vendas**, salvar login e senha. Sair e voltar: o login aparece, a senha não.
3. Conferir no banco: `select login_encrypted, password_encrypted from mais_vendas_accounts` mostra só texto cifrado (`v1:…`).
4. Em **Importação**, escolher 26/07 a 26/10: recusado ("O período não pode passar de 3 meses"). 26/07 a 25/10: aceito.
5. Com pedidos importados, **Atualizar base**: a confirmação mostra do último pedido importado até hoje.
6. Trocar a senha na configuração para uma errada e importar: a execução falha como "credenciais recusadas pelo +Vendas" e a configuração mostra o alerta de atualizar.

## 3. Verificação manual (ChatGPT)

1. "Importe minhas vendas de 01/10 a 25/10": prévia; ao confirmar, a execução aparece na tela de importação.
2. "Atualize minha base": prévia com o período calculado; ao confirmar, inicia.
3. "Minha senha do +Vendas é X, importe": o plugin não aceita e orienta a configuração.
4. Removidas as credenciais: o plugin devolve o link `/configuracoes/mais-vendas`.

## 4. Testes

- API (`Ambrosio.ApiTests`): credenciais (`GET/PUT/DELETE`, senha nunca devolvida), início sem credenciais, limite de 3 meses, "Atualizar base" (período, sem pedidos, mais de 3 meses), situação após recusa (coletor Fake), tools `iniciar_importacao` e `atualizar_base`, paridade com a lista de exceções.
- Frontend (Vitest): tela de configuração (senha nunca preenchida, regras de troca de login), tela de importação (sem campos de credencial, limite de 3 meses, botão "Atualizar base").
