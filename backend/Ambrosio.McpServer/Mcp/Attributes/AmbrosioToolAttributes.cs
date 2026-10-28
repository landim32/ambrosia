namespace Ambrosio.McpServer.Mcp.Attributes;

/// <summary>
/// Escopos exigidos pela tool (feature 007, R6). É a fonte única: o <c>ScopeChallengeFilter</c>
/// confere o token por ele e o <c>ToolDescriptorFilter</c> publica o <c>securitySchemes</c> a partir
/// dele. Com mais de um escopo, a tool exige todos.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class AmbrosioScopesAttribute(params string[] scopes) : Attribute
{
    public IReadOnlyList<string> Scopes { get; } = scopes;
}

/// <summary>
/// Tool anônima (<c>noauth</c>, R7): roda sem token e não acessa dado de usuário. Toda tool precisa
/// declarar este atributo ou <see cref="AmbrosioScopesAttribute"/>; sem nenhum dos dois o filtro
/// recusa a chamada (falha fechada).
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class AmbrosioNoAuthAttribute : Attribute
{
}

/// <summary>
/// Operação equivalente da API REST (<c>"GET /customers"</c>), publicada em
/// <c>_meta["ambrosio/endpoint"]</c> para o teste de paridade (R12).
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class AmbrosioEndpointAttribute(string endpoint) : Attribute
{
    public string Endpoint { get; } = endpoint;
}

/// <summary>
/// <c>outputSchema</c> exato da tool, em JSON. Usado quando o schema gerado a partir do tipo não
/// consegue expressar o contrato (por exemplo, <c>additionalProperties: false</c> e <c>pattern</c>
/// da tool de perfil).
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class AmbrosioOutputSchemaAttribute(string json) : Attribute
{
    public string Json { get; } = json;
}
