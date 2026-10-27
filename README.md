# ambrosio

## Storage de imagens

As imagens (feature 004) ficam em um bucket **privado** compatível com S3, e o navegador sobe o
binário direto nele por URL pré-assinada — o arquivo não passa pela API no caminho do cliente.

Development e homologação usam o mesmo bucket do DigitalOcean Spaces (`emagine`, região `nyc3`, pasta `ambrosio/`);
produção usa bucket próprio. Só o par de chaves é segredo e nunca vai no `appsettings`:

```powershell
# .env (homolog) e ambiente do shell (Development)
OBJECT_STORAGE_ACCESS_KEY=<spaces access key>
OBJECT_STORAGE_SECRET_KEY=<spaces secret key>
OBJECT_STORAGE_BUCKET=emagine
OBJECT_STORAGE_PUBLIC_ORIGIN=https://emagine.nyc3.digitaloceanspaces.com
```

Sem as duas chaves a API recusa subir. Bucket e regra CORS estão explicados em
[`deploy/README.md`](deploy/README.md); o guia ponta a ponta é o
[`quickstart.md`](specs/004-s3-image-storage/quickstart.md).