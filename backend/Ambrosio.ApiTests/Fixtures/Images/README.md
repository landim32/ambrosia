# Fixtures binários de imagem (feature 004)

Arquivos usados por `Controllers/ImagesControllerTests.cs`. Todos abaixo de 50 KB e gerados — nenhum
é foto real, para o repositório não carregar dado de pessoa nenhuma.

| Arquivo | Bytes | O que é | Usado em |
|---|---|---|---|
| `small.jpg` | 3082 | JPEG 320x240 xadrez, com EXIF (`Make`/`Model`, `Orientation = 6` e IFD GPS) | envio direto (US1/US2) e gravação pelo servidor (US3) |
| `other.jpg` | 3255 | JPEG 321x241, outros cores, **sem EXIF** | segunda imagem distinta (hash diferente) para os testes de deduplicação e de isolamento |
| `small.png` | 1001 | PNG 320x240 | aceite de `image/png` (US3 cenário 4) |
| `small.webp` | 1720 | WebP 320x240 (RIFF/WEBP) | aceite de `image/webp` (US3 cenário 4) |

Os arquivos foram gerados uma vez e são versionados como estão; não há gerador no repositório.

Não edite os arquivos no editor: `ImagesControllerTests` calcula o SHA-256 do próprio arquivo e o
declara como o hash do envio, então qualquer mudança de byte é consistente por si só.
