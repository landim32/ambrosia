using Ambrosia.API.Extensions;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Ambrosia.API.Swagger;

/// <summary>
/// Esconde <c>POST /auth/dev-login</c> da documentação em Production (research §2). A rota já
/// responde 404 nesse ambiente; removê-la do Swagger evita que um leitor da documentação
/// conclua que existe um atalho de autenticação em produção (SC-001).
/// </summary>
public class DevLoginDocumentFilter : IDocumentFilter
{
    /// <summary>Caminho exato gerado pelo Swashbuckle para a ação de dev-login.</summary>
    public const string DevLoginPath = "/auth/dev-login";

    private readonly IHostEnvironment _environment;

    public DevLoginDocumentFilter(IHostEnvironment environment)
    {
        _environment = environment;
    }

    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        if (_environment.IsDevLoginEnabled())
        {
            return;
        }

        swaggerDoc.Paths.Remove(DevLoginPath);
    }
}
