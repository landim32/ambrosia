namespace Ambrosio.DTO.Settings;

/// <summary>
/// Configuração do servidor MCP do plugin do ChatGPT (feature 007, R6/R7/R17).
/// </summary>
public class McpSettings
{
    /// <summary>
    /// URL do recurso protegido. Vira o <c>aud</c> exigido em todo access token (R5), o
    /// <c>resource</c> do Protected Resource Metadata e o valor exato que o parâmetro
    /// <c>resource</c> do authorize/token tem de bater. Obrigatória, sem barra no final, e
    /// <c>https</c> fora de Development.
    /// </summary>
    public string ResourceUrl { get; set; } = string.Empty;

    /// <summary>
    /// Origens de navegador aceitas no endpoint <c>/mcp</c> (R17). Chamadas do ChatGPT são
    /// servidor-a-servidor e não mandam header <c>Origin</c>; uma requisição de navegador de fora
    /// desta lista é recusada com 403, o que fecha a porta de DNS rebinding que o transporte HTTP
    /// do MCP exige fechar. Vazia significa "nenhuma origem de navegador" (o recomendado em
    /// produção, onde o único cliente é o ChatGPT).
    /// </summary>
    public List<string> AllowedOrigins { get; set; } = [];

    /// <summary>
    /// Origem pública do frontend, usada para montar o link que a tool <c>iniciar_importacao</c>
    /// devolve à vendedora (R9). Nunca vem de argumento de tool: um <c>redirect</c> controlável pelo
    /// chamador transformaria a tool em gerador de link de phishing.
    /// </summary>
    public string FrontendBaseUrl { get; set; } = string.Empty;
}
