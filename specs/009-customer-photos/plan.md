# Implementation Plan: Fotos de clientes no estilo rede social

**Branch**: `009-customer-photos` | **Date**: 2026-10-29 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/009-customer-photos/spec.md`
**Revisão**: 2026-10-29 — sem processamento de imagem no servidor (decisão do usuário registrada em
[research.md](research.md) e no topo de [spec.md](spec.md)).

## Summary

Cada cliente ganha um álbum de fotos (`customer_photos`) apoiado nas `stored_images` da feature 004, com
descrição, favorita, origem e, opcionalmente, uma foto de perfil quadrada recortada **no navegador** pela
vendedora (colunas novas em `customers`).

**O servidor não decodifica imagem.** Toda foto entra pelos caminhos que a 004 já publica e valida:

- bytes da tela por `POST /images` (multipart) → `imageId`;
- bytes da conversa por `IImageService.StoreFromServerAsync(…, ImageSource.ChatGpt)` → `imageId`;
- e as rotas desta feature só relacionam `imageId` ↔ cliente.

O recorte do perfil é produzido por `<canvas>` no navegador e enviado como arquivo pela mesma rota. A grade
mostra o **original**, sem miniatura. A API ganha **8 operações em 5 caminhos** aninhados em `/customers/{id}` (nenhuma recebe bytes) e o MCP
ganha **8 ferramentas**, com `anexar_fotos_cliente` recebendo arquivos da conversa por
`openai/fileParams`.

No frontend, o perfil da cliente vira um perfil de rede social:
- avatar redondo com o recorte, que abre a foto original;
- abas **Fotos** (padrão) e **Pedidos**;
- grade quadrada com rolagem infinita;
- área discreta "Arraste para incluir uma foto";
- visualizador com coração, descrição, navegação e editor de recorte (`react-easy-crop`).

## Technical Context

**Language/Version**: C# 12 / .NET 8.0 (backend: API e host do MCP); TypeScript 5.x / React 18 + Vite 6 (frontend)
**Primary Dependencies**: as existentes (EF Core 9 + Npgsql, FluentValidation, AWSSDK.S3, ModelContextProtocol 2.3, OpenIddict).
**Nenhuma biblioteca nova no backend.** No frontend entram `react-easy-crop` 5.x e `@radix-ui/react-tabs`
1.x (Radix já é a base de `Avatar`/`Select`/`Separator`), conforme research R9.

**Storage**: PostgreSQL, `AmbrosioContext`. Migração `AddCustomerPhotos`:
- tabela `customer_photos` (uma linha = um arquivo);
- 2 colunas de perfil em `customers` (`profile_photo_id`, `profile_image_id`).

Os binários continuam no storage S3 da 004, gravados exatamente como chegaram.

**Testing**:
- testes de API (`Ambrosio.ApiTests`, skill `dotnet-test-api`) contra a homologação, com os fixtures
  binários já versionados (`Fixtures/Images/*`) e sufixo GUID para o hash não colidir entre execuções;
- testes das tools do MCP e `ParityTests` (8 ferramentas novas, sem exceção de paridade);
- testes unitários do frontend (Vitest + Testing Library);
- sem testes de unidade no backend e sem E2E (Princípio VIII).

**Target Platform**: containers Linux (homolog local no WSL2, produção via GitHub Actions); SPA servida pelo nginx do gateway.
**Project Type**: web-service + SPA (aplicação existente).
**Performance Goals**:
- foto incluída e visível na grade em menos de 5 s numa conexão comum (SC-001) — agora em duas chamadas
  (upload + registro), o que cabe no orçamento;
- primeira página da grade em até 2 s (SC-002). **Risco registrado**: sem miniatura, uma página de 24
  originais pode levar dezenas de MB; a mitigation é a página de 24 + `loading="lazy"` + `IntersectionObserver`
  (research R3);
- coração refletido em menos de 1 s, com atualização otimista (SC-008).

**Constraints**:
- arquivo guardado byte-idêntico ao recebido, sem corte nem regravação no servidor (FR-004, SC-003);
- 10 MB por arquivo, o `MaxUploadBytes` da 004 (sem limite de megapixels: não há decodificação);
- só URL pré-assinada, nada público (FR-026);
- isolamento por `user_id` (FR-025);
- nenhuma detecção de rosto (FR-012);
- a validação de upload da 004 não é alterada (decisão do usuário sobre assinatura de arquivo).

**Scale/Scope**:
- dezenas a centenas de fotos por cliente;
- 1 tabela nova, 2 colunas novas, 8 operações novas em 5 caminhos e 3 rotas alteradas (lista e detalhe de clientes, detalhe do pedido);
- 8 tools novas e 2 alteradas (`obter_cliente`, `listar_clientes`);
- 1 página reestruturada e 6 componentes novos.

Nenhum item ficou como NEEDS CLARIFICATION: as dúvidas da spec foram resolvidas na sessão de Clarifications
e na revisão de escopo de 2026-10-29; as técnicas estão no [research.md](research.md) (R1 a R14).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio | Avaliação | Situação |
|---|---|---|
| I. Skills obrigatórias | `dotnet-architecture` (entidade `CustomerPhoto`, colunas em `Customer`, repositório, serviço, DTOs, migração e DI), `dotnet-fluent-validation` (validadores), `dotnet-test-api` (testes da API e do MCP), `react-architecture` (types, service, context, hook e provider de fotos), `react-modal` e `react-alert`, `wsl-docker` (homolog). `dotnet-graphql` não se aplica: não há GraphQL. `dotnet-env` não se aplica: não há variável nem ambiente novo | PASS |
| II. Stack fixa | Tabs são shadcn/ui (Radix); estilos só com Tailwind; Fetch API no service novo, com `FormData` pelo `apiClient` e progresso por estado, sem XHR (R9); estado em Context API. `react-easy-crop` é biblioteca de domínio de UI — não substitui ORM, CSS, estado nem bundler. **Nenhum pacote novo no backend** | PASS |
| III. Casing | Arquivos novos em `src/Contexts`, `src/Services`, `src/hooks`, `src/types`, `src/lib` e `src/components/customerPhotos`, com o casing existente | PASS |
| IV. Convenções | DTOs com `[JsonPropertyName]` camelCase; enum serializado em camelCase; interfaces TS com `interface`; tools e argumentos em pt-BR, como as existentes | PASS |
| V. Banco | `customer_photos` e colunas em snake_case; PK `customer_photo_id`; FKs `fk_{pai}_{filho}` com `ClientSetNull`; exclusão feita pelo serviço, sem cascata (data-model §5) | PASS |
| VI. Autenticação e segurança | Controller com `[Authorize]`; usuário só do `sub`; toda leitura de repositório recebe `userId` como primeira cláusula; 404 para recurso alheio; tools com escopo (`customers:read`, `images:write` — ambos já existem), sem `userId`, com `confirmar` em anexar e excluir; download da conversa pelo `SafeFileDownloader` (só https, sem IP privado); links só pré-assinados. **EXIF/GPS deixaram de ser removidos** (FR-005 revogada pelo usuário), o que é uma redução de privacidade aceita e documentada | PASS com ressalva documentada |
| VII. Ambientes e testes de API | Todas as rotas novas e alteradas com testes de API; ciclo fecha com homolog reconstruída e suíte inteira passando | PASS |
| VIII. Testes | Só testes de API no backend e unitários no frontend | PASS |

**Resultado**: PASS, com a ressalva de privacidade da linha VI registrada na spec (FR-005) e no research.

**Re-check pós-design**: data-model, contratos e UI não acrescentam violação. Pontos conferidos:
- referência `customers` ↔ `customer_photos` com `ClientSetNull` (sem cascata, sem check constraint de dependência entre tabelas);
- **nenhuma rota multipart nova**: o upload é a rota `POST /images` da 004, já limitada e auditada;
- `source=chatGpt` recusado na rota REST, para que uma integração não se passe pelo MCP;
- paridade ferramenta↔rota nominal: nenhuma rota de foto fica sem ferramenta, e não há rota avulsa de foto
  porque a renovação de URL usa `GET /images/{imageId}` (R10).

## Project Structure

### Documentation (this feature)

```text
specs/009-customer-photos/
├── plan.md              # Este arquivo
├── research.md          # Fase 0: decisões R1–R14 + revisão de escopo
├── data-model.md        # Fase 1: customer_photos, colunas de perfil, DTOs, exclusão
├── quickstart.md        # Fase 1: dependências, migração e verificação
├── contracts/
│   ├── api.md           # 8 operações novas em 5 caminhos, e 3 rotas alteradas
│   ├── mcp-tools.md     # 8 tools novas e 2 alteradas
│   └── ui.md            # Perfil com abas, grade, arraste, visualizador e recorte no navegador
├── checklists/requirements.md
└── tasks.md             # Fase 2 (/speckit.tasks)
```

### Source Code (repository root)

```text
backend/
├── Ambrosio.DTO/
│   ├── CustomerPhotos/                  # CustomerPhotoInfo, CustomerPhotoPage, CustomerPhotoListQuery,
│   │                                    # CustomerPhotoInsertInfo, CustomerPhotoInsertResult,
│   │                                    # CustomerPhotoDescriptionInfo, ProfilePhotoUpdateInfo,
│   │                                    # CustomerProfilePhotoInfo
│   ├── Enums/CustomerPhotoSource.cs     # novo
│   └── Customers/                       # CustomerInfo / CustomerDetailInfo: profilePhoto, photoCount
├── Ambrosio.Domain/
│   ├── Models/CustomerPhoto.cs          # novo
│   ├── Models/Customer.cs               # colunas de perfil, SetProfilePhoto/ClearProfilePhoto
│   ├── Interfaces/ICustomerPhotoService.cs
│   ├── Services/CustomerPhotoService.cs # inclusão, listagem, descrição, favorita, perfil, exclusão
│   └── Validators/                      # CustomerPhotoListQuery, Insert, Description, ProfilePhotoUpdate
├── Ambrosio.Infra.Interfaces/
│   └── Repository/ICustomerPhotoRepository.cs
├── Ambrosio.Infra/
│   ├── Repository/CustomerPhotoRepository.cs
│   ├── Context/AmbrosioContext.cs                  # Fluent API
│   └── Migrations/<ts>_AddCustomerPhotos.cs
├── Ambrosio.Application/Startup.cs                 # DI (nenhum AppService novo)
├── Ambrosio.API/Controllers/CustomerPhotosController.cs   # novo
├── Ambrosio.McpServer/Mcp/Tools/CustomerPhotoTools.cs     # novo; CustomerTools/InsightTools alterados
└── Ambrosio.ApiTests/
    ├── Controllers/CustomerPhotosControllerTests.cs
    ├── McpServer/CustomerPhotoToolsTests.cs
    └── McpServer/ParityTests.cs                    # 8 tools novas

frontend/src/
├── types/customerPhoto.ts
├── types/customer.ts                    # profilePhoto substitui photoUrl
├── Services/apiClient.ts                # opção `formData` (sem Content-Type próprio)
├── Services/customerPhotoService.ts
├── Contexts/CustomerPhotoContext.tsx
├── hooks/useCustomerPhotos.ts
├── lib/cropToFile.ts                    # canvas → File (recorte do perfil)
├── components/ui/tabs.tsx               # shadcn
├── components/customerPhotos/
│   ├── CustomerPhotoGrid.tsx
│   ├── CustomerPhotoDropzone.tsx
│   ├── CustomerPhotoViewer.tsx
│   ├── CustomerPhotoDescription.tsx
│   └── ProfilePhotoCropper.tsx
├── components/customers/CustomerAvatar.tsx   # comentário da HU 010 removido; uso de profilePhoto
├── pages/CustomerProfilePage.tsx              # cabeçalho + Tabs (Fotos | Pedidos)
├── main.tsx                                   # CustomerPhotoProvider
└── i18n/locales/pt-BR.ts                      # namespace customerPhotos

bruno/Customers/Fotos/                         # 7 requisições
plugin/skills/vendas-ambrosio/SKILL.md         # seção "Fotos das clientes"
docs/historias/010-banco-imagens-clientes-chatgpt.md   # Status, Spec 009 e escopo ampliado
```

**Structure Decision**: aplicação web existente (backend .NET em camadas + SPA). A feature acrescenta
arquivos nas camadas já existentes, sem projeto novo e **sem camada de processamento de imagem**: o Domain
fala com `IImageService` (004) e com `ICustomerPhotoRepository`, e nada no servidor conhece formato de
arquivo além do que a 004 já conhece.

## Fases de implementação

1. **Fundação backend**: enum `CustomerPhotoSource`, DTOs, entidade `CustomerPhoto`, colunas em `Customer`,
   Fluent API, migração `AddCustomerPhotos`, repositório e DI. Sem pacote novo.
2. **US1 + US2 (P1)**: serviço de inclusão e listagem, controller (`GET`, `POST`), testes de API. Frontend:
   types, `formData` no `apiClient`, service, context, abas no perfil, grade, área de arraste e visualizador
   só de leitura.
3. **US3 (P1)**: perfil (`PUT`/`DELETE profile-photo`), `profilePhoto` em `GET /customers` e no detalhe,
   `profilePhoto` no cliente do detalhe do pedido, testes. Frontend: avatar com foto, editor de recorte no
   navegador e avatar que abre a original.
4. **US4 (P2)**: descrição, favorita, filtro e exclusão (API, testes e UI).
5. **US5 (P2)**: `CustomerPhotoTools` (8 tools), alterações em `obter_cliente`/`listar_clientes`,
   `ParityTests`, SKILL do plugin.
6. **US6 (P2)**: completa a API para integrações (a maior parte já sai nas fases 2 a 4); coleção Bruno.
7. **Fechamento**: `DeleteAllForUserAsync` com as fotos, HU 010 atualizada, homolog reconstruída, suíte de
   API inteira e testes do frontend.

## Riscos

| Risco | Mitigação |
|---|---|
| Grade pesada sem miniatura (SC-002 em dados móveis) | Página de 24, `loading="lazy"`, `IntersectionObserver`; saída futura registrada em R3 (redimensionar no navegador antes do envio), que é decisão de produto, não deste ciclo |
| EXIF/GPS permanecem no arquivo servido | Aceito explicitamente pelo usuário em 2026-10-29; registrado na FR-005 e no research. Mitigação real existente: o círculo do perfil é um recorte regravado pelo canvas do navegador, que não carrega EXIF |
| Arquivo não-imagem declarado como imagem é aceito | Comportamento da 004 preservado por decisão do usuário; sem execução de código no navegador, apenas imagem quebrada |
| `openai/fileParams` não entregar os arquivos no ChatGPT | `enviar_imagem` já usa `FileArgument`; teste manual no modo desenvolvedor (quickstart §6) antes do merge; o `_meta` vai no `tools/list` (teste automatizado) |
| Duas chamadas por foto (upload + registro) falharem pela metade | A foto só aparece no álbum depois do segundo passo; um `imageId` órfão é recolhido pelo job de limpeza da 004 (`PurgeStaleAsync`) se ficou `pending`, e é inofensivo se ficou `available` sem uso |

## Complexity Tracking

Sem violações da constituição a justificar. Reduções desta revisão: nenhum pacote novo no backend, nenhum
AppService novo, nenhuma rota multipart nova, nenhuma coluna de enquadramento e nenhuma rota de renovação de
URL por foto.
