# MCP para consultar as vendas com uma LLM

Servidor MCP (stdio) em cima da base do doc 02. Somente leitura, em conexão com usuário de banco `SELECT`-only.

## Tools sugeridas

| Tool | Parâmetros | Retorna |
|---|---|---|
| `resumo_periodo` | `inicio`, `fim`, `canal?` | total, atendimentos, peças, PA, VA, % da cota |
| `listar_pedidos` | `inicio`, `fim`, `tipo?`, `canal?`, `cliente?`, `limite` | pedidos com cliente e valor |
| `detalhe_pedido` | `numero`, `data?` | itens, cliente, status |
| `buscar_cliente` | `nome` ou `cpf` | perfil + resumo de compras |
| `historico_cliente` | `cliente_id` | pedidos, itens, ticket médio, última compra |
| `clientes_inativos` | `dias`, `min_compras?` | clientes sem compra há N dias (reativação) |
| `top_produtos` | `inicio`, `fim`, `limite` | referências mais vendidas |
| `devolucoes_cancelamentos` | `inicio`, `fim` | motivos e totais |
| `status_coleta` | — | data da última captura e contagens |

Exemplos de perguntas que ficam possíveis: "quanto vendi na semana passada no online?", "quais clientes não compram há 90 dias e gastaram mais de R$ 5 mil?", "qual cliente mais devolveu?".

## Boas práticas

- Não expor o CPF completo; retornar mascarado (`994.***.***-68`) e deixar o hash servir para junção.
- Parâmetros tipados (zod); queries parametrizadas; nada de SQL livre vindo do modelo.
- Limitar linhas (`limite` ≤ 200) e sempre informar `capturado_em`, já que os dados podem mudar até o fechamento.
- Escrever as descrições das tools em português, com exemplos, para a LLM escolher bem.

## Esqueleto (TypeScript)

```ts
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import { z } from 'zod';

const server = new McpServer({ name: 'ambrosia-vendas', version: '0.1.0' });

server.tool(
  'resumo_periodo',
  'Totais de vendas, atendimentos, PA e VA em um intervalo de datas.',
  { inicio: z.string().date(), fim: z.string().date(), canal: z.string().optional() },
  async ({ inicio, fim, canal }) => {
    const rows = await db.query(/* SELECT ... WHERE order_date BETWEEN $1 AND $2 */);
    return { content: [{ type: 'text', text: JSON.stringify(rows) }] };
  }
);

await server.connect(new StdioServerTransport());
```

## Ordem de implementação

1. Criar a base (doc 02, fase 1).
2. Rodar o bot (doc 03) e conferir os totais contra a tela (R$ 123.300,58 / 49 pedidos no período 26/09–25/10/2026).
3. Subir o MCP com `resumo_periodo`, `listar_pedidos` e `buscar_cliente`.
4. Adicionar a fase 2 (contatos, perfil, interações) e as tools de clientes.
