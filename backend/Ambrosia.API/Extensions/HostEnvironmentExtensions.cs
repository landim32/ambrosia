namespace Ambrosia.API.Extensions;

/// <summary>
/// Regras por ambiente da API. <c>Homolog</c> é o ambiente do docker compose local
/// (<c>docker-compose.yml</c>), usado para validar a stack completa antes da produção.
/// </summary>
public static class HostEnvironmentExtensions
{
    /// <summary>Nome do ambiente de homologação (<c>ASPNETCORE_ENVIRONMENT=Homolog</c>).</summary>
    public const string Homolog = "Homolog";

    /// <summary>
    /// <c>POST /auth/dev-login</c> fica disponível em Development e Homolog, para o quickstart e
    /// para os testes HTTP rodarem nos dois ambientes. Em Production responde 404 e some do
    /// Swagger.
    /// </summary>
    /// <param name="environment">Ambiente atual.</param>
    /// <returns><c>true</c> em Development ou Homolog.</returns>
    public static bool IsDevLoginEnabled(this IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment(Homolog);
}
