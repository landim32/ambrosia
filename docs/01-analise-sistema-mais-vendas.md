# Análise do sistema +Vendas (Somalabs)

Levantamento feito em 25/10/2026, somente leitura, com a conta da vendedora S482. Nada foi cadastrado, alterado ou criado.

## 1. Visão geral

- Front-end SPA: `https://maisvendas.somalabs.com.br`
- API: `https://api.mais-vendas.somalabs.com.br` (único endpoint observado: `POST /salesperson/orders/client-data`, 10 chamadas por carga da lista; o corpo e a resposta **não foram inspecionados**)
- Analytics de terceiros: Google Analytics e Microsoft Clarity (irrelevantes para o bot)
- Layout mobile-first, tema escuro, sem tabelas HTML (listas de cards)
- Marca no parâmetro das URLs de detalhe: `brands=9`

## 2. Mapa de telas

| Rota | Menu | Conteúdo |
|---|---|---|
| `/cotaPiloto?utm_campaign=68018&utm_source=maisvendas` | Cota | Dashboard de metas e lista de pedidos do período |
| `/omniOperational` | Operacional Omni | Hub com 2 itens: Pedidos Realizados e Pedidos Extras |
| `/storeOmni` | Operacional Omni > Pedidos Realizados | KPIs omni e lista de pedidos (ids tipo `v1161148crb-01`) |
| `/extraOrders` | Operacional Omni > Pedidos Extras | Lista vazia ("Sem pedidos"); link para SinOMS |
| `/detailOrder/{pedido}/{vendedor}/{AAAA-MM-DD}/{id?}?brands=9` | (clique em um pedido) | Cliente, CPF, valores, itens |
| `/maisCarrinho` | Carrinhos | Vazio (0); a tela mostrou "Erro ao carregar carrinhos"; botão "Novo carrinho" (**não acionar**) |
| `somabox.somalabs.com.br` | BOX | **Sistema separado** com login próprio, fora do escopo |

Rodapé do menu: Central de Ajuda, Suporte, Sair.

## 3. Tela Cota (`/cotaPiloto`)

**Seletor de período:** Mês (Cota) | Semana | Dia | Personalizado. O mês de cota vai de 26/set a 25/out (fechamento no dia 25, não no dia 1). Há setas para navegar entre períodos.

**Card de meta:**
- Valor vendido no período
- Cota do mês (R$ 114.999,97) com % atingido (107,22%)
- Supercota (R$ 149.499,96) com % atingido (82,48%)
- Mensagem de status ("Último dia do período") e gráfico de evolução (FusionCharts)

**Cards de venda (Geral, Física, Online):**
- Valor total
- Atendimentos (número de pedidos)
- Peças vendidas
- PA = peças por atendimento
- VA = valor por atendimento (ticket médio)

**Lista "Pedidos do mês":**
- Abas: Todos, Vendas, Devoluções, Cancelamentos (com contadores)
- Filtro "Exibir somente": Todas, Linx, Online, Vitrine, Box, Vintage (esses são os **canais/origens** de venda)
- Busca por texto
- Paginação por botão "Ver mais"
- Cada linha tem: nome do cliente, nº do pedido, data (`24 Out`, sem ano), valor
- Devoluções e cancelamentos aparecem com valor negativo e total próprio

Aviso do sistema: até o fim do primeiro dia útil após o fechamento da competência, as vendas podem sofrer alteração. **O bot precisa tratar dados como mutáveis** (ver doc 03).

## 4. Tela Operacional Omni > Pedidos Realizados (`/storeOmni`)

- Seletor de período igual ao da Cota
- Total (R$), Pedidos faturados, **TMA** (tempo médio de atendimento, ex.: `8h05`), Taxa de cancelamento (%)
- Lista com abas Todos / Vendas / Devoluções
- Cada linha tem só data, nº do pedido e valor (**sem nome de cliente** na listagem; o cliente vem no detalhe)

## 5. Tela de detalhe (`/detailOrder/...`)

Pedido de venda:
- Nº do pedido, nome do cliente, **CPF**
- Canal (ex.: "Venda Linx"), valor total, data de faturamento, status (ex.: Faturado)
- Itens: nome, referência (`42CA500UIE_12182`), preço, tamanho (38, P) e imagem

Pedido cancelado (URL tem `&isCancelled=true`):
- Valor negativo, data da venda original, data do cancelamento, status Cancelado
- Itens com valor negativo

Devolução: não foi aberta em detalhe; presume-se igual ao cancelamento, **a confirmar**.

## 6. Dados que o sistema NÃO expõe (relevantes para a base nova)

Telefone, e-mail, endereço, data de nascimento, forma de pagamento, loja/filial, desconto, cor do item e histórico do cliente não apareceram em nenhuma tela vista. Isso é o que a base própria vai enriquecer.

## 7. Pontos em aberto

1. Significado do 4º segmento da URL de detalhe (`45812850`): provavelmente id interno do pedido ou do cliente. Interceptar a API para confirmar.
2. Formato real do corpo/resposta de `/salesperson/orders/client-data`. Se a API aceitar o token da sessão, o bot pode consumi-la direto em vez de raspar a tela (mais robusto). Verificar termos de uso antes.
3. Os ids `41900001`, `70000003` (devoluções) e `v...crb-01` (online) sugerem origens/numerações diferentes. Mapear por canal.
4. O mesmo nº de pedido (`41900001`) aparece em datas diferentes (28/set e 05/out), então **o nº do pedido sozinho não é chave única**.
5. A tela de Carrinhos retornou erro de carga; vazia ou falha de API? Indeterminado.
