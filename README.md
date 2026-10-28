# ambrosio

## Storage de imagens

As imagens (feature 004) ficam em um bucket **privado** compatível com S3, e o navegador sobe o
binário direto nele por URL pré-assinada — o arquivo não passa pela API no caminho do cliente.

Development e homologação usam o mesmo bucket do DigitalOcean Spaces (`emagine`, região `nyc3`, pasta `ambrosio/`);
produção usa bucket próprio. Só o par de chaves é segredo e nunca vai no `appsettings`:

```powershell
# .env (homolog) e ambiente do shell (Development)
S3_ACCESS_KEY=<spaces access key>
S3_SECRET_KEY=<spaces secret key>
```

O bucket e a origem pública dele (CSP do frontend) são fixos em cada `docker-compose*.yml`.

Sem as duas chaves a API recusa subir. Bucket e regra CORS estão explicados em
[`deploy/README.md`](deploy/README.md); o guia ponta a ponta é o
[`quickstart.md`](specs/004-s3-image-storage/quickstart.md).