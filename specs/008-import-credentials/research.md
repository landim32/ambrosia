# Research: Credenciais do +Vendas guardadas, importação pelo plugin e "Atualizar base" (008)

**Data**: 2026-10-28 | **Spec**: [spec.md](spec.md)

Cada item: **Decisão**, **Motivo**, **Alternativas consideradas**.

---

## R1. Onde guardar as credenciais

- **Decisão**: em `mais_vendas_accounts` (feature 006), que já é 1:1 com a vendedora e guarda o vínculo da conta e o bloqueio por recusas. Colunas novas: `login_encrypted`, `password_encrypted`, `credentials_saved_at`, `password_changed_at` e `credentials_status`. Migração `AddMaisVendasCredentials`.
- **Motivo**: o estado de credenciais, vínculo e bloqueio é lido e escrito nos mesmos pontos (início da importação e login no +Vendas). Uma tabela a mais repetiria o 1:1 e a busca por `user_id`.
- **Alternativas**: tabela própria `mais_vendas_credentials` (mais isolada, mas mais um join no caminho quente e outra entidade para o mesmo dono); cofre externo (custo e operação desproporcionais para o uso próprio).

## R2. Criptografia

- **Decisão**: AES-256-GCM (cifra autenticada) com uma **chave dedicada** `MaisVendas__CredentialsKey` (base64 de 32 bytes, variável de ambiente `AMBROSIO_MAIS_VENDAS_CREDENTIALS_KEY`), nonce aleatório de 12 bytes por gravação e **dados associados** `mais-vendas:{user_id}:{campo}`. Formato gravado: `v1:` + base64(nonce ‖ tag ‖ cifra). Novo AppService `IMaisVendasCredentialCipherAppService` (Encrypt/Decrypt) na Infra.
- **Motivo**: a senha precisa voltar a texto claro para logar no +Vendas, então hash não serve; GCM detecta adulteração. A chave dedicada separa o dano de um vazamento da chave do CPF. Os dados associados amarram a cifra à vendedora e ao campo: copiar a coluna de uma vendedora para outra (ou login para senha) faz a decifração falhar. O prefixo `v1:` deixa espaço para rotação de chave sem migração de esquema.
- **Alternativas**: reaproveitar a chave do CPF (`CpfProtection__EncryptionKey`) — menos um segredo a operar, mas um vazamento abriria CPFs e credenciais juntos; criptografia no banco (pgcrypto) — a chave passaria pelo SQL e pelos logs do PostgreSQL.
- **Startup**: API e host do MCP recusam iniciar sem a chave válida (mesmo padrão do CPF, Princípio VI). O pipeline grava o GitHub Secret no `.env.prod`.

## R3. Quando decifrar

- **Decisão**: só em dois pontos: (1) na execução da importação, no worker, imediatamente antes do login no +Vendas; (2) o **login** (não a senha) para a tela de configuração da própria vendedora. O `ImportJob` deixa de carregar login e senha: o worker lê e decifra as credenciais quando a execução começa e as descarta ao terminar.
- **Motivo**: a senha em claro existe só durante o login de uma execução, nunca na fila nem em DTO. Uma execução que estava na fila usa a senha vigente quando começa (troca de senha antes de começar vale; durante, não — spec, Edge Cases).
- **Alternativas**: decifrar no início (`StartAsync`) e enfileirar como hoje — a senha ficaria em memória enquanto a execução aguarda na fila.

## R4. Contrato da API de credenciais

- **Decisão**: `GET`, `PUT` e `DELETE /imports/mais-vendas/credentials` (detalhes em [contracts/api.md](contracts/api.md)). O `GET` devolve `login`, `hasPassword`, `passwordChangedAt` e `status` (`none`, `valid`, `needsUpdate`); o `PUT` recebe `login` e `password` (opcional só quando o login não muda); nenhuma resposta tem campo de senha.
- **Motivo**: FR-001 a FR-006. Mantém tudo sob o recurso de importação, que é o único consumidor.
- **Alternativas**: `/users/me/settings` genérico (mistura configuração de perfil com segredo de terceiro).

## R5. Início da importação e "Atualizar base"

- **Decisão**: `POST /imports/mais-vendas/runs` passa a receber só `startDate`, `endDate` e `dryRun` (sem `login`/`password`). Nova rota `POST /imports/mais-vendas/runs/update-base` com `dryRun`, que calcula o período no servidor (último pedido importado do +Vendas, inclusive, até hoje em São Paulo). `GET /imports/mais-vendas/defaults` passa a informar `hasCredentials`, `credentialsStatus`, `maxRangeMonths` e `updateBase` (`available`, `startDate`, `endDate`, `reason`).
- **Motivo**: o período do "Atualizar base" é calculado em um lugar só (servidor), igual para tela e plugin (FR-013/FR-017). A tela mostra a prévia a partir dos defaults e confirma.
- **Recusas** (todas 400/409 com mensagem): sem credenciais (`"Cadastre as credenciais do +Vendas na configuração."`), sem pedido importado (`"Ainda não há pedidos importados para atualizar."`), intervalo maior que 3 meses no "Atualizar base" (`"A última importação foi há mais de 3 meses. Importe em partes de até 3 meses, escolhendo as datas."`), credenciais marcadas como "precisam ser atualizadas" **não** bloqueiam (o +Vendas pode aceitar; a regra de bloqueio por recusas segue valendo).
- **Alternativas**: o cliente calcular o período e chamar a rota normal (duplicaria a regra em tela e plugin).

## R6. Período máximo de 3 meses

- **Decisão**: `MaisVendas:MaxRangeMonths` (padrão 3) substitui `MaisVendas:MaxRangeDays`. Regra: `endDate <= startDate.AddMonths(MaxRangeMonths).AddDays(-1)`. Mensagem única: `"O período não pode passar de 3 meses."`. Vale no validator do servidor, na tela (mesma conta) e no plugin.
- **Motivo**: "3 meses" em dias variaria entre 89 e 92; a conta por mês casa com a percepção da vendedora (26/07 a 25/10 aceito, 26/07 a 26/10 recusado — spec, Edge Cases).
- **Alternativas**: 92 dias fixos (aceitaria períodos que a vendedora não chamaria de 3 meses).

## R7. Situação das credenciais após recusa

- **Decisão**: quando o +Vendas recusa o login (fluxo existente `RegisterRefusedLogin`), `credentials_status` passa a `needsUpdate`; login aceito volta a `valid`; salvar nova senha volta a `valid`. O bloqueio por recusas seguidas (feature 006) não muda.
- **Motivo**: FR-007. A tela de configuração e o plugin conseguem orientar a vendedora sem expor nada.

## R8. Origem da execução

- **Decisão**: `import_runs` ganha `started_via` (1 = tela, 2 = plugin) e `kind` (1 = período, 2 = atualizar base). O host do MCP chama o mesmo `IImportRunService` com `startedVia = Mcp`.
- **Motivo**: Key Entities da spec ("registrar se foi iniciada pela tela, pelo plugin ou pelo Atualizar base") e relatório/histórico.

## R9. Plugin do ChatGPT

- **Decisão**: `iniciar_importacao` passa a **iniciar** a execução (`inicio`, `fim`, `simular?`, `confirmar`) e devolve o id; nova tool `atualizar_base` (`simular?`, `confirmar`) → `POST /imports/mais-vendas/runs/update-base`. Sem credenciais, ambas devolvem erro com o link `{Mcp:FrontendBaseUrl}/configuracoes/mais-vendas`. Continuam recusando qualquer argumento fora do schema (login, senha), e a auditoria registra período, modo e resultado. **Não** há tool para credenciais: as rotas `/imports/mais-vendas/credentials` entram numa lista explícita de exceções do teste de paridade (FR-019).
- **Motivo**: FR-016 a FR-020. Revisa o R9 da feature 007 (link para a tela), que existia só porque a senha não podia passar pela conversa.
- **Alternativas**: manter o link (não atende à spec).

## R10. Tela de configuração

- **Decisão**: rota `/configuracoes/mais-vendas` no frontend, com entidade nova pela skill `react-architecture` (`types`, `service`, `context`, `hook`). Formulário: login (preenchido), senha (sempre vazia, placeholder "senha cadastrada" quando houver), salvar e remover (com confirmação em modal, skill `react-modal`), alerta de "precisam ser atualizadas" (skill `react-alert`). Link no menu e na tela de importação.
- **Motivo**: FR-001/FR-003/FR-009.

## R11. Exclusão da conta

- **Decisão**: não existe exclusão de conta no Ambrosio hoje; quando existir, as credenciais vão junto por estarem em `mais_vendas_accounts` (removida com o usuário). Remoção explícita pela vendedora: `DELETE /imports/mais-vendas/credentials` zera as colunas.
- **Motivo**: Edge Case da spec, sem fluxo novo a criar nesta feature.

## R12. Revisão de decisões anteriores

- A feature 006 (FR-005/FR-006) proibia gravar login e senha; a 007 (R9) trocava a importação do plugin por um link. Esta feature as substitui por: credenciais gravadas só criptografadas (R2), decifradas só no login (R3), nunca devolvidas (R4) e nunca aceitas pela conversa (R9). A constituição não proíbe guardar credencial de terceiro, mas não diz como; a [emenda proposta](constitution-amendment.md) acrescenta a regra explícita.
