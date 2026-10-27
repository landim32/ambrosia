# Fixtures binários de imagem (feature 004)

Arquivos usados por `Controllers/ImagesControllerTests.cs`. Todos abaixo de 50 KB e gerados — nenhum
é foto real, para o repositório não carregar dado de pessoa nenhuma.

| Arquivo | Bytes | O que é | Usado em |
|---|---|---|---|
| `small.jpg` | 3082 | JPEG 320x240 xadrez, **com EXIF**: `Make`/`Model`, `Orientation = 6` e IFD GPS (`0x8825`) com latitude/longitude | envio direto (US1/US2), recusa de tipo, e a prova de que o caminho do servidor remove EXIF/GPS (US3) |
| `other.jpg` | 3255 | JPEG 321x241, outros cores, **sem EXIF** | segunda imagem distinta (hash diferente) para os testes de deduplicação e de isolamento |
| `small.png` | 1001 | PNG 320x240 | aceite de `image/png` (US3 cenário 4) |
| `small.webp` | 1720 | WebP 320x240 (RIFF/WEBP) | aceite de `image/webp` (US3 cenário 4) |
| `fake.jpg` | 191 | **PDF** mínimo (`%PDF-1.4`) com nome `.jpg` | recusa por conteúdo: o nome mente e os *magic bytes* entregam (FR-005, US1 cenário 6, US3 cenário 3) |

## Como regenerar

Gerados por um console à parte com `SixLabors.ImageSharp` (o projeto de testes **não** referencia a
biblioteca — ele só lê os bytes). O programa cabe em um arquivo:

```csharp
// Image<Rgba32> Make(int w, int h, Color a, Color b) -> xadrez de 16px, como acima.
var exif = new ExifProfile();
exif.SetValue(ExifTag.Make, "Ambrosia Test");
exif.SetValue(ExifTag.Model, "Fixture 004");
exif.SetValue(ExifTag.Orientation, (ushort)6);
exif.SetValue(ExifTag.GPSLatitudeRef, "S");
exif.SetValue(ExifTag.GPSLatitude, new[] { new Rational(23), new Rational(33), new Rational(150, 60) });
exif.SetValue(ExifTag.GPSLongitudeRef, "W");
exif.SetValue(ExifTag.GPSLongitude, new[] { new Rational(46), new Rational(17), new Rational(300, 60) });
// jpeg.Metadata.ExifProfile = exif; jpeg.Save("small.jpg", new JpegEncoder { Quality = 85 });
```

Comandos (a pasta do projeto console fica fora do controle de versão — ela só precisa do pacote
`SixLabors.ImageSharp` e de `TargetFramework net8.0`):

```powershell
dotnet run --project <pasta-do-gerador> -c Debug C:\repos\ambrosia\backend\Ambrosia.ApiTests\Fixtures\Images
```

`-c Debug` é obrigatório: o ImageSharp 3.2.0 exige chave de licença da Six Labors no build **Release**
(ver `specs/004-s3-image-storage/decisions.md`, seção "tasks · licença ImageSharp").

Depois de regenerar, confira os invariantes que os testes assumem:

```powershell
# small.jpg tem de ter EXIF e o IFD GPS; fake.jpg tem de começar com %PDF.
Select-String -Path small.jpg -Pattern 'Exif' -Encoding Byte -Quiet
```

Não edite os arquivos no editor: `ImagesControllerTests` calcula o SHA-256 do próprio arquivo e o
declara como o hash do envio, então qualquer mudança de byte é consistente por si só — mas um
`small.jpg` **sem** EXIF faria o teste da US3 passar por vazio, não por ter removido os metadados.
