# Bot de coleta com Playwright

Objetivo: extrair as vendas e os clientes da conta da vendedora no +Vendas e gravar na base (doc 02). **Somente leitura.**

## Regras de segurança do bot

1. **Nunca** clicar em "Novo carrinho", "Sair" ou qualquer botão que crie ou altere dados. Usar uma allowlist de rotas e seletores.
2. **Não** seguir o item de menu **BOX** (domínio `somabox`, sistema separado e com login próprio).
3. Credenciais só em variáveis de ambiente (`.env` fora do git), nunca no código nem nesta documentação.
4. Respeitar o ritmo: 1 a 2 requisições por segundo no máximo, com pausas aleatórias. Rodar fora do horário de pico.
5. Conferir os termos de uso da Somalabs antes de automatizar; a conta é da usuária, mas o acesso automatizado pode ter restrições.

## Stack

Node.js + TypeScript + `@playwright/test` (só a lib `playwright`), `better-sqlite3` ou `pg`, `zod` para validar o que foi extraído.

```
bot/
  .env                # MV_USER, MV_PASS, DATABASE_URL  (no .gitignore)
  auth/state.json     # storageState (no .gitignore)
  src/login.ts
  src/scrape-cota.ts
  src/scrape-omni.ts
  src/scrape-detail.ts
  src/persist.ts
  src/run.ts
```

## Etapa 0: tentar a API antes de raspar a tela

O front chama `POST https://api.mais-vendas.somalabs.com.br/salesperson/orders/client-data`. Com o Playwright, escutar `page.on('response')` durante a navegação e salvar o JSON em `raw_snapshot`. Se a resposta já trouxer pedido, cliente, CPF e itens, **o bot fica muito mais simples e estável**: basta capturar essas respostas enquanto a UI pagina, sem depender de seletores. Só raspar o DOM para o que a API não trouxer (KPIs).

```ts
page.on('response', async (res) => {
  if (res.url().includes('/salesperson/orders/client-data') && res.ok()) {
    const payload = await res.json();
    await saveRaw('client-data', { reqBody: res.request().postData() }, payload);
  }
});
```

## Etapa 1: login e sessão

Não achei a tela de login (a sessão já estava aberta no navegador). Implementar assim e **ajustar os seletores na primeira execução**:

```ts
import { chromium } from 'playwright';
import fs from 'fs';

const STATE = 'auth/state.json';

export async function getContext() {
  const browser = await chromium.launch({ headless: true });
  const ctx = await browser.newContext(
    fs.existsSync(STATE) ? { storageState: STATE } : {}
  );
  const page = await ctx.newPage();
  await page.goto('https://maisvendas.somalabs.com.br/cotaPiloto?utm_campaign=68018&utm_source=maisvendas');
  if (!(await page.getByText(/Olá,/).isVisible({ timeout: 8000 }).catch(() => false))) {
    // TODO: seletores do formulário de login (não observados)
    await page.getByLabel(/usu[aá]rio|login/i).fill(process.env.MV_USER!);
    await page.getByLabel(/senha/i).fill(process.env.MV_PASS!);
    await page.getByRole('button', { name: /entrar/i }).click();
    await page.getByText(/Olá,/).waitFor();
    await ctx.storageState({ path: STATE });
  }
  return { browser, ctx, page };
}
```

## Etapa 2: períodos

O mês de cota vai de 26 a 25. Gerar a lista de períodos a coletar e usar as setas `‹ ›` ao lado da data (ou "Personalizado") para navegar. Coletar o mês corrente, mais N meses para trás até a lista vir vazia.

## Etapa 3: lista de pedidos (`/cotaPiloto`)

Para cada período e cada filtro de canal (`Linx`, `Online`, `Vitrine`, `Box`, `Vintage`):

1. Clicar em `Filtrar` e escolher o canal (radio). O canal vira a coluna `channel`.
2. Para cada aba (`Vendas`, `Devoluções`, `Cancelamentos`):
   - Clicar na aba e depois em `VER MAIS` até o botão sumir.
   - Ler os `listitem`: cliente, nº pedido, data (`DD Mon`), valor.
3. Conferir: o contador da aba (`Vendas (40)`) deve ser igual ao nº de linhas lidas; o total exibido deve ser igual à soma. **Se divergir, falhar a execução** em vez de gravar dado parcial.
4. Ler os KPIs (cota, supercota, atendimentos, peças, PA, VA) para `period_kpi`.

Dica de seletores: os chips são `button` com o texto do tipo `Devoluções (6)`; as linhas são `listitem`. Preferir `getByRole`/`getByText` a classes CSS (provavelmente geradas).

## Etapa 4: lista Omni (`/storeOmni`)

Mesmo processo; a listagem **não traz o cliente**, só data, pedido e valor, então o cliente vem do detalhe. Capturar também Total, Pedidos faturados, TMA e Taxa de cancelamento.

## Etapa 5: detalhe do pedido

Abrir clicando na linha (a URL contém um 4º segmento que não dá para derivar). Rota: `/detailOrder/{pedido}/{vendedor}/{AAAA-MM-DD}/{id}?brands=9`; cancelados têm `&isCancelled=true`. Extrair:

| Campo | Origem |
|---|---|
| order_number | título / URL |
| customer.name, customer.cpf | bloco "Detalhes" |
| channel ("Venda Linx") | bloco de valor |
| total, status | bloco de valor |
| order_date | "Faturamento" (ou "Venda Original" + "Data de Cancelamento") |
| items[] | cards: nome, `Ref:`, preço, tamanho, imagem |

Cada item tem no máximo um tamanho; o texto `Ref:` precisa ser removido do prefixo.

## Etapa 5b: fotos das peças

1. No detalhe, ler o `src` de cada `img` do card do item (descartar ícones e avatares) e gravar em `product_image` com `status = 'pending'`, ligado ao `sku_ref`.
2. Em uma etapa separada, baixar só as pendentes. Se a URL for pública, usar `fetch`; se exigir sessão, usar `ctx.request.get(url)` para reaproveitar os cookies.
3. Calcular `sha256`, gravar o arquivo em `data/images/...`, preencher mime, dimensões e bytes, e marcar `downloaded`. Em erro, marcar `failed` e tentar de novo na próxima execução (máx. 3).
4. Pular o download se já existir imagem do mesmo `sku_ref` + `source_url`.
5. Mesmo ritmo lento do resto do bot; imagens são o maior volume de tráfego.

## Etapa 6: persistência

- Normalizar valores `R$ 1.234,56` e `-R$ 1.234,56` para decimal.
- Normalizar CPF (só dígitos) e calcular `cpf_hash`.
- Datas sem ano: deduzir o ano pelo período; confirmar pela URL do detalhe.
- `UPSERT` em `sales_order` por `(salesperson_id, order_number, order_date, kind)`. Se o `source_hash` mudar, atualizar e registrar a mudança (a origem altera vendas até o 1º dia útil após o fechamento).
- Re-coletar sempre a competência anterior até 1 dia útil depois do dia 25.

## Etapa 7: execução

- Incremental diário: mês corrente + competência anterior (se ainda aberta).
- Carga histórica: uma vez, mês a mês, com pausa entre meses.
- Agendar via Task Scheduler (Windows) ou cron.
- Logs com contagem por período e canal; alerta se a contagem cair ou divergir do contador da tela.
- Tratar `Erro ao carregar ...` (toast vermelho visto em Carrinhos) com retry e backoff, e abortar após 3 falhas.

## Fragilidades conhecidas

- Interface muda sem aviso: manter testes de fumaça que validam os seletores.
- O 4º segmento da URL de detalhe é desconhecido; navegar sempre por clique.
- Ids de pedido repetem entre datas (`41900001`).
- Dados incompletos de devolução em detalhe (não inspecionado).
- Sessão expira: renovar `storageState` ao receber redirecionamento para login.
