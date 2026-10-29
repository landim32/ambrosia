# Contrato do MCP: Fotos de clientes

**Feature**: `009-customer-photos` | **Revisão**: 2026-10-29 (sem processamento no servidor)

**8 ferramentas novas**, uma por operação REST (paridade nominal nos `ParityTests`, sem exceção). Saída no
formato padrão do host (`ToolResultFactory`: `isError` + texto legível, listas com teto de 200 e
`tem_mais`, enumeração camelCase, `structuredContent`).

**Nenhuma ferramenta recebe, decodifica ou recorta imagem.** O que a conversa faz é baixar o arquivo pelo
`SafeFileDownloader` da feature 007, gravá-lo pelo `IImageService.StoreFromServerAsync` existente
(`ImageSource.ChatGpt`) e anexar o `imageId` resultante à cliente. O recorte do perfil é impossível na
conversa — não há navegador —, então `definir_foto_perfil` aponta a foto do álbum e o círculo usa o arquivo
dela (research R4).

**Escopos** (nenhum é novo; `images:write` já existe desde a 004/007):

| Operação | Escopo |
|---|---|
| `listar_fotos_cliente` | `customers:read` |
| as demais | `images:write` (FR-027) |

**Confirmação** (`confirmar`, padrão `WriteToolHelper`): obrigatória em `anexar_fotos_cliente` (prévia com
cliente, quantidade, tipos e tamanhos) e em `excluir_foto_cliente` (FR-027). As reversíveis
(`favoritar_foto_cliente`, `desfavoritar_foto_cliente`, `descrever_foto_cliente`, `definir_foto_perfil`,
`remover_foto_perfil`) executam direto — são o que a vendedora pediu na conversa.

---

## 1. `listar_fotos_cliente` — `GET /customers/{customerId}/photos`

`ReadOnly=true, Destructive=false, Idempotent=true, OpenWorld=false`. Escopo `customers:read`.
`{ cliente_id, favoritas?: false, cursor?: number, tamanho?: 24 }`.

```json
{
  "itens": [
    { "fotoId": 1204, "imagemId": 5731, "descricao": "provando o vestido azul", "favorita": true,
      "perfil": true, "origem": "web", "incluidaEm": "2026-10-29T10:00:00Z" }
  ],
  "total": 37,
  "proximoCursor": 1180,
  "tem_mais": true
}
```

Sem URLs de arquivo: o modelo não precisa delas (padrão da HU 010 e de `obter_cliente`), e evitar URL
assinada na conversa reduz a chance de vazar link temporário. `temFotoPerfil` e `totalFotos` já saem de
`obter_cliente`.

`total` é o tamanho do **álbum inteiro**, mesmo com `favoritas=true` (o serviço conta as fotos da cliente,
não as da página filtrada) — serve para a vendedora saber quanto falta, não quantos corações há.

## 2. `anexar_fotos_cliente` — `POST /customers/{customerId}/photos`

`ReadOnly=false, Destructive=false, Idempotent=true, OpenWorld=true`. Escopo `images:write`.
`_meta["openai/fileParams"] = ["fotos"]` (atributo `AmbrosioFileParamsAttribute` +
`ToolDescriptorFilter`; o SDK do MCP não expõe essa chave como propriedade tipada), porque o ChatGPT
entrega arquivos como link de download, nunca base64.

`{ cliente_id, fotos: FileArgument[], descricao?: string, confirmar: false }` — `fotos` aceita **até 10** por
chamada.

Prévia (`confirmar=false`): baixa nada? **Baixa sim, só para medir**: a prévia faz o download de cada
arquivo pelo `SafeFileDownloader` (que já impõe https, endereços públicos e o teto de bytes) para informar
tipo e tamanho reais, e **não grava nada** — o mesmo contrato de `enviar_imagem`.

```json
{
  "previa": { "cliente": "Maria Souza", "fotos": [
    { "nome": "IMG_0143.jpg", "tipo": "image/jpeg", "tamanhoBytes": 2481132 } ] },
  "gravado": false,
  "aviso": "Nada foi gravado. Confirme para gravar."
}
```

Confirmado:

```json
{
  "gravado": true,
  "cliente": "Maria Souza",
  "fotos": [
    { "resultado": "guardada", "fotoId": 1204, "imagemId": 5731 },
    { "resultado": "ja_existia", "fotoId": 1188, "imagemId": 5702 },
    { "resultado": "guardada_tambem_em_outra_cliente", "fotoId": 1205 },
    { "resultado": "recusada", "motivo": "O arquivo está grande demais (limite de 10 MB)." }
  ],
  "guardadas": 2, "recusadas": 1
}
```

- `motivo` é a mensagem **da 004** (`ImageRejectionReason`), literal — esta feature não cria frase de
  recusa (research R2).
- `guardada_tambem_em_outra_cliente` **não nomeia** a outra cliente: o serviço devolve o sinal
  booleano (`alsoInOtherCustomers`), e dizer *qual* exigiria uma leitura extra por arquivo. O edge case
  da spec pede o aviso, não o nome — quem precisa do nome consulta `listar_clientes`.
- Uma recusa não interrompe as demais (FR-021, cenário US5-4).
- `cliente_id` que não é da vendedora (ou não existe) → `isError: true`, **nada gravado** (FR-028).
- A ambiguidade de nome **não** é resolvida por esta ferramenta: ela recebe `cliente_id`, como todas as
  outras de escrita. Quem identifica a cliente é o modelo, com `listar_clientes`/`buscar_cliente` — que
  é justamente onde o cenário US5-2 ("duas Maria Souza") acontece, e onde a resposta já traz as opções.

## 3. `descrever_foto_cliente` — `PUT /customers/{customerId}/photos/{photoId}/description`

`{ cliente_id, foto_id, descricao? }` — **sem `confirmar`**: é reversível e o host fecha o schema
(`additionalProperties: false`), então enviar `confirmar` aqui seria recusado. 500 caracteres máximos (400 com a
mensagem da API). Resultado: `{ gravado: true, foto: {…} }`.

## 4. `favoritar_foto_cliente` — `PUT /customers/{customerId}/photos/{photoId}/favorite`

`{ cliente_id, foto_id }`. Executa direto.

## 5. `desfavoritar_foto_cliente` — `DELETE /customers/{customerId}/photos/{photoId}/favorite`

`{ cliente_id, foto_id }`. Executa direto. Uma ferramenta por verbo (PUT e DELETE) porque a paridade é 1:1
com a rota (R11).

## 6. `definir_foto_perfil` — `PUT /customers/{customerId}/profile-photo`

`{ cliente_id, foto_id }` — **sem `confirmar`** (reversível). **Sem enquadramento**: na conversa não há como produzir o
recorte quadrado, então o círculo usa o arquivo da foto com `object-cover` no navegador. A vendedora é
avisada na resposta:

```json
{
  "gravado": true,
  "cliente": "Maria Souza",
  "fotoPerfil": { "fotoId": 1204, "imagemId": 5731 },
  "aviso": "A foto de perfil ficou sem recorte quadrado: para enquadrar no rosto, use a tela do Ambrosio."
}
```

Se a vendedora já gravou um recorte antes pela tela, `definir_foto_perfil` **não** o descarta: ele é
reaproveitado quando a foto informada é a mesma de que o recorte saiu; trocando de foto, o recorte velho é
largado (vira candidato a órfão). Assim o enquadramento escolhido na tela sobrevive a uma frase na conversa.

## 7. `remover_foto_perfil` — `DELETE /customers/{customerId}/profile-photo`

`{ cliente_id }` — **sem `confirmar`** (reversível). A foto continua no álbum.

## 8. `excluir_foto_cliente` — `DELETE /customers/{customerId}/photos/{photoId}`

`{ cliente_id, foto_id, confirmar: false }`. **`Destructive = true`**; recusa executar sem `confirmar=true`
(FR-027). Prévia devolve a foto, `descricao` e `favorita`, para a vendedora confirmar de que se trata.
A resposta avisa quando o arquivo ficou no storage porque ainda é usado:

```json
{
  "gravado": true, "fotoId": 1204, "excluida": true,
  "arquivo": "removido",
  "perfil": "a cliente voltou a mostrar as iniciais"
}
```

`arquivo` ∈ `"removido" | "mantido (em uso)"` (edge case "mesma foto em duas clientes"; se era a foto de
perfil, o campo `perfil` aparece).

---

## Erros (mesma mensagem da API, §10 do contrato REST)

`ValidationException` (descrição/`pageSize`), `NotFoundException` ("Foto não encontrada" / "Cliente não
encontrada"), recusa de upload com a frase da 004, `StorageUnavailableException` ("O armazenamento de
imagens não está disponível agora…") e `confirmar=false` em ferramenta destrutiva ("A ferramenta … exige
confirmação antes de executar."). Nenhum erro devolve URL assinada, chave de objeto ou cabeçalho de storage
(FR-021).

## Metadados (paridade 1:1 com a API)

`[AmbrosioScopes]` e `[AmbrosioEndpoint]` iguais aos do quadro de operações acima. A paridade é nominal nos
dois lados: nenhuma rota de foto fica sem ferramenta e nenhuma ferramenta aponta para rota inexistente.
