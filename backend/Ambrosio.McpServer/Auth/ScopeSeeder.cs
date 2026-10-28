using Ambrosio.DTO.Settings;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;

namespace Ambrosio.McpServer.Auth;

/// <summary>
/// Semente idempotente dos escopos do authorization server (feature 007, T016, data-model §1.4).
/// </summary>
/// <remarks>
/// Roda uma vez no arranque e sobe antes de qualquer endpoint: sem as linhas em
/// <c>oauth_scopes</c>, a tela de consentimento não teria o que listar (FR-003/FR-004) e o
/// <c>scopes_supported</c> da descoberta sairia vazio.
/// <para>
/// Idempotente de propósito: cada escopo só é criado se o nome ainda não existir, e um escopo já
/// existente <b>não</b> tem seu texto sobrescrito. Reescrever o texto a cada arranque apagaria
/// qualquer ajuste feito direto na base de produção.
/// </para>
/// <para>
/// O parâmetro <c>resources</c> é o que amarra cada escopo à URL do MCP: é por ele que o OpenIddict
/// põe <c>aud = Mcp:ResourceUrl</c> no access token sem handler extra (R5), e por isso a semente
/// depende da configuração, não de constante.
/// </para>
/// </remarks>
public sealed class ScopeSeeder(
    IServiceProvider services,
    IOptions<McpSettings> mcpSettings,
    ILogger<ScopeSeeder> logger) : BackgroundService
{
    /// <summary>
    /// Os onze escopos do plugin (data-model §1.4), com o texto que a vendedora lê na tela de
    /// consentimento. Ordem irrelevante; nomes são o contrato com as tools.
    /// </summary>
    public static readonly (string Name, string DisplayName, string Description)[] Scopes =
    [
        ("openid", "Identificar a conta", "Confirma que a conversa está ligada à sua conta do Ambrosio."),
        ("email", "Ler seu e-mail cadastrado", "Usa o endereço associado à conta para identificar a vendedora."),
        ("profile", "Ler seu nome", "Usa o nome cadastrado para cumprimentos e para assinar as ações."),
        ("offline_access", "Manter a conexão ativa", "Permite renovar o acesso sem pedir login a cada conversa."),
        ("customers:read", "Ver clientes", "Consulta cadastros, histórico e resumo de compras dos seus clientes."),
        ("customers:write", "Cadastrar e editar clientes", "Cria clientes e muda o nome de clientes existentes."),
        ("orders:read", "Ver pedidos", "Consulta pedidos, itens, devoluções e cancelamentos."),
        ("orders:write", "Registrar pedidos", "Cria e altera pedidos na sua base."),
        ("images:write", "Enviar imagens", "Grava fotos de produto vindas da conversa."),
        ("imports:read", "Ver importações do +Vendas", "Consulta o andamento e o resultado das importações."),
        ("imports:write", "Gerenciar importações do +Vendas", "Cancele importações em andamento e leia o progresso.")
    ];

    /// <summary>
    /// Só os nomes, na ordem da tabela acima — é o que vai a <c>scopes_supported</c> nos dois
    /// documentos de descoberta (T019) e ao <c>ToolDescriptorFilter</c> (T023), para ninguém
    /// redigitar a lista e divergir da semente.
    /// </summary>
    public static readonly string[] ScopeNames = [.. Scopes.Select(s => s.Name)];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // O manager só existe com o container montado; BackgroundService roda depois do Build, então
        // o scope aqui é o lugar certo (e um construtor com IOpenIddictScopeManager direto também
        // funcionaria, mas resolver o scope dentro do serviço evita custo no arranque do host).
        using var scope = services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();

        var resource = mcpSettings.Value.ResourceUrl;
        if (string.IsNullOrWhiteSpace(resource))
        {
            logger.LogError(
                "Mcp__ResourceUrl está vazio: os escopos seriam semeados sem recurso, e os tokens "
                + "emitidos não receberiam o `aud` que o MCP exige. Semeador cancelado.");
            return;
        }

        var created = 0;
        foreach (var (name, displayName, description) in Scopes)
        {
            if (await manager.FindByNameAsync(name, stoppingToken) is not null)
            {
                continue;
            }

            // DisplayName e Description são propriedades simples; Resources é coleção somente-leitura
            // (aberta para Add), então o descritor é montado em duas etapas.
            var descriptor = new OpenIddictScopeDescriptor
            {
                Name = name,
                DisplayName = displayName,
                Description = description
            };

            descriptor.Resources.Add(resource);

            await manager.CreateAsync(descriptor, stoppingToken);

            created++;
        }

        logger.LogInformation(
            "Escopos do plugin verificados: {Created} criados, {Total} no total.", created, Scopes.Length);
    }
}
