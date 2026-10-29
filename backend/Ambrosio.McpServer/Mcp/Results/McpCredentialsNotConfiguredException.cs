namespace Ambrosio.McpServer.Mcp.Results;

/// <summary>
/// A vendedora pediu algo que exige credenciais do +Vendas sem tê-las cadastrado (feature 008,
/// FR-004/FR-018). O texto é sempre o mesmo e leva o link da tela de configuração, porque é a única
/// superfície onde o cadastro acontece — o plugin não oferece tool de credencial nenhuma.
/// <para>
/// Vive aqui, e não no Domain, porque a frase é do contrato do plugin: a API responde a mesma situação
/// com <c>IMaisVendasCredentialService.NOT_SAVED_MESSAGE</c> (sem link, pois a vendedora já está na
/// tela). <see cref="ToolResultFactory"/> a trata como erro de domínio, então a prévia e a execução
/// recusadas saem pelo mesmo caminho — <c>isError</c> com a mensagem, e auditoria do desfecho.
/// </para>
/// </summary>
public sealed class McpCredentialsNotConfiguredException : Exception
{
    public McpCredentialsNotConfiguredException(string configurationLink)
        : base($"Cadastre as credenciais do +Vendas na configuração do Ambrosio: {configurationLink}")
    {
        ConfigurationLink = configurationLink;
    }

    /// <summary>URL da tela de configuração, montada com <c>Mcp:FrontendBaseUrl</c>.</summary>
    public string ConfigurationLink { get; }
}
