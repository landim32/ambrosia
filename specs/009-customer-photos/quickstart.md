# Quickstart: Fotos de clientes no estilo rede social

**Feature**: `009-customer-photos` | **Revisão**: 2026-10-29 — sem processamento de imagem no servidor
(ver [research.md](research.md)).

Roteiro para subir e verificar a feature. Não há variável de ambiente nova: valem o storage da 004 (MinIO em
Dev e Homolog, Spaces em produção) e o `MaxUploadBytes` existente. **Nenhum segredo novo** e **nenhum
pacote novo no backend**.

## 1. Dependências

```bash
# backend: nada. A feature usa IImageService e as rotas de imagem que a feature 004 já publica.

# frontend: duas dependências, e nenhuma substituição de stack
cd frontend && npm install react-easy-crop@^5 @radix-ui/react-tabs@^1
```

## 2. Migração

```bash
cd backend
dotnet ef migrations add AddCustomerPhotos -p Ambrosio.Infra -s Ambrosio.API
```

Uma tabela (`customer_photos`) e duas colunas em `customers` (`profile_photo_id`, `profile_image_id`). Em
Homolog e Production, a migração é aplicada pelo compose e pelo job `migrate` do deploy, como nas features
anteriores.

## 3. Development (sem Docker)

A API em Development precisa dos segredos do storage, que só existem no shell de quem a lança
(`ObjectStorage__AccessKey`/`__SecretKey` não estão em `appsettings.Development.json`) — suba-a com
`backend/scripts/run-api.ps1` nesse shell e derrube com `stop-api.ps1`.

```bash
cd frontend && npm run dev
```

1. Entre, abra uma cliente em `/clientes/{id}`: a aba **Fotos** vem aberta e vazia.
2. Arraste 3 fotos do computador para "Arraste para incluir uma foto": elas aparecem no topo, sem pedir nada.
   Cada uma são duas chamadas — `POST /images` (bytes) e `POST /customers/{id}/photos` (registro).
3. Arraste a mesma foto de novo: aparece o aviso "Essa foto já estava no álbum." (`created: false`).
4. Arraste um `.heic` ou um `.txt`: o navegador recusa antes de enviar, com o nome do arquivo no aviso.
5. Abra uma foto: ela aparece inteira, na proporção original; navegue com ← →, marque o coração e escreva
   uma descrição.
6. "Usar como foto de perfil": posicione o círculo no rosto e salve. O navegador gera o quadrado de
   512 × 512 no canvas, envia por `POST /images` e registra com `PUT .../profile-photo`. O avatar do
   cabeçalho e o da lista de clientes passam a mostrar o recorte. Tocar no avatar abre a foto original.
7. Exclua a foto de perfil: a cliente volta às iniciais, e o recorte que sobrou sem uso sai do storage.

## 4. Verificar que o arquivo não é tocado no servidor

A feature promete gravar os bytes exatamente como chegaram (FR-004/SC-003). A checagem é o hash:

```bash
sha256sum foto.jpg   # (Windows: certutil -hashfile foto.jpg SHA256)

curl -s -H "Authorization: Bearer $TOKEN" -F "file=@foto.jpg" \
  http://localhost:5000/images | jq '.imageId, .width, .height'

curl -s -H "Authorization: Bearer $TOKEN" \
  "http://localhost:5000/customers/45/photos?pageSize=1" | jq '.items[0] | {imageId, url}'

curl -s "<url>" -o baixada.jpg
sha256sum baixada.jpg        # esperado: IGUAL ao de foto.jpg
jq -n '{width, height}'      # (na resposta de GET /images/{id}: width/height seguem null — a 004 não decodifica)
```

O que **não** se verifica mais, porque saiu do escopo com a FR-005: remoção de EXIF/GPS e aplicação de
orientação no arquivo guardado. A orientação correta na tela vem do navegador
(`image-orientation: from-image`), e o recorte do perfil, regravado pelo canvas, nasce sem metadado.

## 5. Homolog e testes de API (Princípio VII)

```bash
# skill wsl-docker — a distro nesta máquina é "Ubuntu" (não "Ubuntu-24.04": wsl -l -v confirma;
# o nome documentado na skill falha com WSL_E_DISTRO_NOT_FOUND)
wsl -d Ubuntu -- bash -lc "cd /mnt/c/repos/ambrosio && docker compose up -d --build"
cd backend
$env:ApiBaseUrl='http://localhost:8080/api'
dotnet test Ambrosio.ApiTests --filter "FullyQualifiedName~CustomerPhotos|FullyQualifiedName~McpServer"
dotnet test Ambrosio.ApiTests     # suíte inteira no fim do ciclo
cd ../frontend && npm test
```

**Medido em 2026-10-29:** a stack sobe, o migrator aplica `AddCustomerPhotos` (o log traz
`Applying migration '20261029143519_AddCustomerPhotos'` → `Done.`) e o filtro de fotos passa **35/35**.
O compose avisa de um container órfão (`ambrosio-homolog-db`, parado há horas): o banco usado é o
remoto do `.env`, não ele — `docker compose ps` mostra api/gateway/mcp `Up` com o `db` `Exited`.

### As duas coisas que a suíte inteira mostrou, com causa

A rodada completa leva **mais de 30 min** (esperas de importação e testes de TTL que dormem de
propósito) e **só uma por vez**, porque todas as coleções dividem o banco da homolog. Na rodada de
2026-10-29 apareceram três falhas em suítes **pré-existentes**, e o log do container da API deu a causa
em vez de deixar suposição:

| Sintoma | SQLSTATE / causa |
|---|---|
| `POST /customers` → 500 `"An exception has been raised that is likely due to a transient failure"` (`ReadToolsTests.ListOver200Rows`) | **53300 — `remaining connection slots are reserved for roles with the SUPERUSER attribute`**: o Postgres gerenciado atingiu o teto de conexões sob as coleções em paralelo. Não é defeito de SQL. |
| `ImportToolsTests.AtualizarBase_...` e `IniciarImportacao_Confirm_...` → corrida terminando `Failed` | **23505 — `duplicate key value violates unique constraint "ux_import_runs_user_active"`**: duas importações ativas da mesma vendedora na mesma janela. A janela da corrida aumenta quando o banco está no teto acima. |

Contexto que explica por que isso aparece aqui e não no pipeline: **a CI não roda `dotnet test`
nenhum** — `.github/workflows` só tem `create-release`, `deploy-prod` e `version-tag` —, e não existe
`xunit.runner.json`, então o paralelismo é o padrão do xUnit (coleções em paralelo). A suíte de API é
executada só à mão, por quem fecha o Princípio VII.

O que a 009 fez com essa informação: o `POST /customers` voltou a ser leve. Antes, cada criação consultava
o álbum para preencher `profilePhoto`/`photoCount`; uma cliente recém-criada não tem foto nenhuma por
construção, então esse caminho não faz mais leitura de foto. Reduzir pressão no caminho que falhou é o
ajuste que competia à feature; mexer no teto de conexões do banco ou no paralelismo da suíte **não
compete** — é decisão de ambiente, registrada aqui para quem decide.

## 6. MCP (ChatGPT)

1. No ChatGPT com o conector Ambrosio, envie 2 fotos: "são da Maria Souza, provando a saia verde".
2. Com duas "Maria Souza" na base, o assistente pergunta qual é. Depois mostra a prévia (nome, tipo e
   tamanho de cada arquivo) e pede confirmação.
3. Confirme. As fotos aparecem no álbum com a descrição e a origem "conversa".
4. "Marca a primeira como favorita e usa como foto de perfil": o coração e o perfil aparecem na tela. O
   perfil vindo da conversa fica **sem recorte dedicado** (não há navegador na conversa), e a resposta da
   ferramenta avisa isso — para enquadrar no rosto, é na tela.
5. `tools/list` (Bruno, `bruno/Mcp/Tools/Tools List.bru`) mostra `anexar_fotos_cliente` com
   `_meta.openai/fileParams = ["fotos"]`.

## 7. Produção

Nada novo em secrets, nada novo em dependência de runtime. Depois do deploy:

- confirme o espaço em disco (o deploy da 008 falhou por disco cheio) — esta feature **não** acrescenta
  nada à imagem; a única mudança de artefato são os dois pacotes npm no bundle do frontend;
- inclua uma foto numa cliente de teste, confira o avatar na lista e confira que o hash do arquivo servido é
  o do arquivo enviado;
- monitore o tráfego da grade: sem miniatura, uma página de 24 fotos baixa originais (risco registrado no
  plan.md e no research R3).
