# Contrato de UI: fotos de clientes

**Feature**: `009-customer-photos` | Frontend React 18 + Vite 6 + Tailwind 4 + shadcn/ui
**Revisão**: 2026-10-29 — sem processamento no servidor: a grade mostra o **original**, e o recorte do
perfil é produzido **aqui no navegador** (canvas), não no `PUT` (research R3/R4).

Arquitetura pela skill `react-architecture`: `types/customerPhoto.ts`, `Services/customerPhotoService.ts` (Fetch, sobre `apiRequest`), `Contexts/CustomerPhotoContext.tsx`, `hooks/useCustomerPhotos.ts` e provider registrado no `main.tsx`. Os bytes sobem pela rota de imagem que já existe (`POST /images`), com uma opção `formData` acrescentada ao `apiClient` — o `Content-Type` fica a cargo do navegador, porque serializar `FormData` é o bug clássico. Avisos com `sonner` (skill `react-alert`) e confirmação com o `ConfirmModal` existente (skill `react-modal`). Textos em `i18n/locales/pt-BR.ts`, no namespace `customerPhotos`.

---

## 1. Perfil da cliente (`/clientes/:id`, `CustomerProfilePage`)

```text
┌────────────────────────────────────────────┐
│  ( foto )   Maria Souza                    │  ← avatar xl: recorte redondo; toque abre a original
│             ***.982.247-**  · 12 compras   │
│             [Ajustar foto de perfil]       │  ← só com perfil definido
├──────────────┬─────────────────────────────┤
│   ▣ Fotos    │   ☰ Pedidos                 │  ← Tabs; Fotos é a padrão (FR-017)
├──────────────┴─────────────────────────────┤
│ ┌ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ┐ │
│   ⇪ Arraste para incluir uma foto          │  ← área discreta (FR-020)
│ └ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ┘ │
│  [Todas] [♥ Favoritas]                     │
│ ┌─────┬─────┬─────┐                        │
│ │ ♥   │     │     │                        │  ← grade 3 colunas, quadrados (FR-018/022)
│ ├─────┼─────┼─────┤                        │
│ │     │  ◌  │     │                        │  ← ◌ = foto em envio
│ └─────┴─────┴─────┘                        │
└────────────────────────────────────────────┘
```

- **Abas**: `Tabs` do shadcn/ui (`@radix-ui/react-tabs`). A aba vai na URL: sem parâmetro = Fotos; `?aba=pedidos` = Pedidos. Os parâmetros que a lista de pedidos já usa continuam valendo dentro da aba Pedidos. O conteúdo atual de pedidos passa, sem mudança, para dentro da aba.
- **Cabeçalho**: `CustomerAvatar size="xl"` com `photoUrl = profilePhoto.url`. Com perfil, o avatar vira botão (`aria-label="Ver foto original de Maria Souza"`) que abre o visualizador na foto de origem (FR-014, US3-4). Sem perfil, mostra as iniciais e não é clicável (US3-5).

## 2. Grade (`CustomerPhotoGrid`)

- 3 colunas em qualquer largura (como num perfil de rede social), `gap-0.5`, células `aspect-square` com `<img src={photo.url} class="object-cover size-full" loading="lazy" decoding="async" alt={description ?? 'Foto de <nome> de <data>'}>`. O recorte quadrado é só visual (FR-018) — **não existe miniatura**: a grade mostra o arquivo original (R3), e o `loading="lazy"` com página de 24 é o que segura o tráfego.
- Coração preenchido no canto superior direito das favoritas (FR-022).
- Ordem da API: mais recente primeiro. Fotos em envio entram no topo com um indicador de estado.
- Rolagem infinita: sentinela com `IntersectionObserver` pede `nextCursor` (US1-7). Esqueleto (`Skeleton`) de 9 células no primeiro carregamento.
- Filtro **Todas / Favoritas** (FR-010, US4-6): troca a lista no contexto e mantém a posição de cada filtro enquanto a página está aberta.
- **Vazia** (US1-6): "Ainda não há fotos da Maria." com a área de inclusão em destaque normal.
- Cada célula é um `<button>` que abre o visualizador naquela foto.

## 3. Área de inclusão (`CustomerPhotoDropzone`)

- Uma linha discreta: borda tracejada `border-border`, texto `text-muted-foreground text-sm` e ícone `ImagePlus`, com o texto **"Arraste para incluir uma foto"** (FR-020). No toque, "Toque para incluir uma foto" (`@media (pointer: coarse)`).
- **Arraste**: `dragenter` na aba inteira destaca a área (borda `border-brand`, fundo `bg-brand/5`); `drop` aceita vários arquivos. Itens que não são arquivo (texto, links) são ignorados.
- **Toque/clique**: abre `<input type="file" multiple accept="image/jpeg,image/png,image/webp" hidden>`.
- **Envio**: cada arquivo vira um item de fila, com até 3 envios simultâneos, em **dois passos** — `POST /images` (multipart, `source` do arquivo) e `POST /customers/{id}/photos` com `{ imageId, source: 'web' }` — sem descrição e sem perguntas (US2-1, R7).
  - Antes de enviar, o navegador recusa na hora tipo fora da lista ou tamanho acima de 10 MB, com toast "IMG.heic: formato não aceito" (edge case "arraste de algo que não é foto"). O servidor confere de novo do lado dele (FR-003 na redação revista: mime declarado + tamanho + hash, como a 004 já faz).
  - Durante o envio, a célula provisória mostra a miniatura local (`URL.createObjectURL`), escurecida e com um spinner (FR-021). O estado da fila mostra "enviando" enquanto dura a primeira chamada e "registrando" entre as duas.
  - Ao concluir, a célula provisória é trocada pela foto da resposta (que usa `URL.revokeObjectURL` para não vazar o blob).
  - Com `created=false`, a célula some e um toast avisa "Essa foto já estava no álbum." (US2-5).
  - Com erro, a célula fica com borda `destructive`, o motivo (mensagem da API, da 004 na primeira chamada e desta feature na segunda) e as ações "Tentar de novo" e "Descartar". As outras fotos seguem (US2-3).

## 4. Visualizador (`CustomerPhotoViewer`, sobre o `Modal`)

```text
┌──────────────────────────────────────────────┐
│ ‹                                         ✕  │
│            ┌──────────────┐                  │
│            │              │                  │  ← original, object-contain, proporção real
│            │   (foto)     │                  │
│            └──────────────┘               ›  │
│ ♡  29 out. 2026 · enviada pela tela          │
│ Provando o vestido azul            ✎ Editar  │
│ [Usar como foto de perfil]   [🗑 Excluir]     │
└──────────────────────────────────────────────┘
```

- Mostra o **original** em `object-contain` com a proporção real (FR-019, US1-3) — sem miniatura intermediária (R3).
- **Navegação**: botões ‹ ›, teclas ← → e deslizar no toque. Ao chegar perto do fim da lista carregada, pede a próxima página (US1-4). `Esc` fecha.
- **Coração**: `aria-pressed`, alterna na hora (otimista). Se a API falhar, volta ao estado anterior com o toast "Não foi possível salvar a favorita." (FR-008, SC-008).
- **Descrição**: texto com "Editar" ao lado (ou "Adicionar descrição" quando vazia). Editar abre um `textarea` com contador `n/500`, Salvar e Cancelar; `Ctrl/Cmd+Enter` salva. Acima de 500 caracteres, o Salvar fica desabilitado com aviso (US4-3 a US4-5).
- **Usar como foto de perfil**: abre o editor de recorte (§5). Se a foto já é o perfil, o rótulo vira "Ajustar enquadramento".
- **Excluir**: `ConfirmModal` com "Excluir esta foto? Essa ação não pode ser desfeita." e, se for o perfil, "A Maria vai voltar a aparecer com as iniciais.". Depois de excluir, mostra a próxima foto, ou fecha se era a última.
- Link vencido: `onError` na `<img>` chama `imageService.getReadUrl(imageId)` — a renovação é a rota `GET /images/{imageId}` da 004, que já cacheia por imagem e compartilha as prometidas em voo — e tenta uma vez (R10).

## 5. Editor de recorte (`ProfilePhotoCropper`, dentro do `Modal`)

- `react-easy-crop` com `aspect={1}`, `cropShape="round"`, `showGrid={false}` e `objectFit="contain"`; zoom por pinça, roda e um controle deslizante de 1× a 4×.
- Posição inicial: o maior quadrado centrado. **Nenhum enquadramento é guardado no servidor** (R4/R5) — reabrir o editor recomeça centrado sobre a foto original do álbum.
- O quadrado não sai da foto (comportamento da biblioteca) e o zoom máximo é limitado para que o lado curto não fique abaixo de 64 px do arquivo exibido (US3-10).
- **Salvar** (dois passos, ambos do lado do navegador antes de qualquer requisição):
  1. um `<canvas>` de 512 × 512 desenha `croppedAreaPixels` sobre o `<img>` já orientado pelo navegador (R2) e `toBlob('image/webp', 0.9)` produz o arquivo; um helper `lib/cropToFile.ts` faz isso e é testado isoladamente;
  2. `POST /images` com esse blob → `imageId`; `PUT /customers/{id}/profile-photo` com `{ photoId, imageId }`.
- Atualiza o avatar do cabeçalho e invalida o item da lista de clientes (US3-2, US3-3). Toast "Foto de perfil atualizada.".
- Falha em qualquer um dos dois passos deixa o editor aberto com o motivo e o botão "Salvar" disponível de novo; nenhum estado de perfil muda antes da resposta do `PUT`.
- **Remover foto de perfil**: botão secundário no editor, só quando a foto é o perfil atual. Chama `DELETE .../profile-photo`.

## 6. Avatar em todo lugar (FR-016)

- `CustomerAvatar` continua com a prop `photoUrl`. O comentário "reservado para a HU 010" sai.
  - `CustomerCard` (lista): `photoUrl={customer.profilePhoto?.url}`.
  - `CustomerProfilePage`: `photoUrl={customer.profilePhoto?.url}`.
  - `OrderDetailPage`: `photoUrl={order.customer.profilePhoto?.url}`.
- Tipos: `CustomerInfo.photoUrl` é substituído por `profilePhoto?: CustomerProfilePhotoInfo | null`.
- O `referrerPolicy="no-referrer"` continua.

## 7. Acessibilidade e responsividade

- Grade, visualizador e editor usáveis só pelo teclado: foco visível, ordem lógica e foco preso no `Modal`.
- Toque: alvos de pelo menos 44 px (coração, setas, Editar).
- 320 px de largura sem rolagem horizontal; a grade continua com 3 colunas.
- `prefers-reduced-motion`: sem animação de troca de foto.

## 8. Testes unitários (Vitest + Testing Library, §VIII)

| Arquivo | Cobre |
|---|---|
| `Services/customerPhotoService.test.ts` | Rotas, `formData` no `apiClient` (sem `Content-Type` próprio), cursor e favoritas |
| `Contexts/CustomerPhotoContext.test.tsx` | Paginação, inserção no topo, dedup (`created=false`), favorita otimista com rollback, exclusão |
| `components/customerPhotos/CustomerPhotoGrid.test.tsx` | Ordem, coração, vazia, esqueleto, `loading="lazy"`, abrir visualizador |
| `components/customerPhotos/CustomerPhotoDropzone.test.tsx` | Drop de vários arquivos, recusa de tipo e tamanho, `input` no clique, os dois passos do envio, estado de erro com "Tentar de novo" |
| `components/customerPhotos/CustomerPhotoViewer.test.tsx` | Setas e teclado, descrição (limite 500), excluir com confirmação, perfil |
| `lib/cropToFile.test.ts` | Canvas → blob: lado, formato pedido e recusa quando o navegador não tem `toBlob` |
| `pages/CustomerProfilePage.test.tsx` | Aba Fotos padrão, `?aba=pedidos`, avatar clicável só com perfil |
