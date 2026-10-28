# Plugin do Ambrosio no ChatGPT

Pacote do plugin de uso próprio (feature 007). O ChatGPT conecta ao servidor MCP do Ambrosio em `https://ambrosioai.site/mcp`, com OAuth pela própria conta do Ambrosio.

| Arquivo | Conteúdo |
|---|---|
| `plugin.json` | Nome, descrição, categoria, site, ícone e logo |
| `mcp.json` | Servidor MCP (HTTP streamable) com autenticação mista e cliente por CIMD |
| `skills/vendas-ambrosio/SKILL.md` | Vocabulário, escolha de tool, gravação com confirmação, segurança e privacidade |
| `assets/` | `icon.png` (ícone do app) e `logo.png` (mascote) |

Nenhum segredo entra no pacote: o ChatGPT se registra sozinho no authorization server (CIMD, com DCR de reserva).

## Conectar no ChatGPT (modo desenvolvedor)

1. ChatGPT → **Settings → Security and login → Developer mode**.
2. `chatgpt.com/plugins` → **+** → **Add custom MCP server**.
3. Nome **Ambrosio**, a descrição do `plugin.json`, URL `https://ambrosioai.site/mcp` e autenticação **OAuth**. Deixe **client ID e secret em branco**.
4. **Create as a plugin** e confira as 28 tools.
5. Numa conversa, `@Ambrosio` e uma pergunta. Na primeira tool que precisa da conta, o ChatGPT oferece **Conectar Ambrosio**: entre com e-mail e senha ou com o Google e autorize as áreas desejadas.
6. Depois de mudar tools no servidor, use **Refresh** na página do plugin.

Se o ChatGPT pedir um escopo que ainda não foi autorizado (por exemplo, gravar clientes), ele mostra de novo a tela de consentimento só com o que falta.

## Homologação pelo túnel

O ChatGPT só conecta em HTTPS público. Para testar a homologação local:

1. Abra um túnel HTTPS para `http://localhost:8080` e anote a URL, por exemplo `https://ambrosio-dev.exemplo.net`.
2. No serviço `mcp` do `docker-compose.yml`, troque `Auth__Issuer` por `https://ambrosio-dev.exemplo.net/auth` e `Mcp__ResourceUrl` por `https://ambrosio-dev.exemplo.net/mcp`, e `Mcp__FrontendBaseUrl` pela mesma origem. Recrie o serviço `mcp`.
3. No Google Cloud, inclua a URL do túnel nas origens JavaScript autorizadas do Client ID (botão do Google na tela de login).
4. Crie o plugin como acima, com a URL do túnel.

## Roteiro de verificação

Pergunte no ChatGPT e anote qual tool ele escolheu (meta: 90% certas, SC-006):

| # | Pergunta | Tool esperada |
|---|---|---|
| 1 | Quem sou eu no Ambrosio? | `perfil_ambrosio` ou `obter_usuario_atual` |
| 2 | O que é o Ambrosio? | `sobre_ambrosio` |
| 3 | Quanto vendi neste mês de cota? | `resumo_periodo` |
| 4 | Qual foi meu PA e meu VA em setembro? | `resumo_periodo` |
| 5 | Quais clientes não compram há 90 dias e já gastaram mais de R$ 5 mil? | `clientes_inativos` |
| 6 | Ache a cliente Ana Lima. | `buscar_cliente` |
| 7 | O que a Ana Lima já comprou? | `historico_cliente` |
| 8 | Quantas devoluções e cancelamentos tive em outubro? | `devolucoes_cancelamentos` |
| 9 | Mostre o pedido 00004751. | `listar_pedidos` + `obter_pedido` |
| 10 | Liste minhas vendas do canal Linx desta semana. | `listar_pedidos` |
| 11 | Cadastre a cliente Bruna Souza, CPF … | `cadastrar_cliente` (prévia, depois confirmação) |
| 12 | Mude o status do pedido 00004751 para Entregue. | `atualizar_pedido` (prévia, depois confirmação) |
| 13 | Guarde esta foto de produto. (com imagem anexada) | `enviar_imagem` |
| 14 | Quero importar minhas vendas do +Vendas. | `sugestao_periodo_importacao` + `iniciar_importacao` (link) |
| 15 | Como está a importação? | `importacao_atual` |

Em nenhuma resposta pode aparecer CPF completo ou pedido da senha do +Vendas.
