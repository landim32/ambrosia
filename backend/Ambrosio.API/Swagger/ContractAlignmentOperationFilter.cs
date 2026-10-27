using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Ambrosio.API.Swagger;

/// <summary>
/// Alinha o documento gerado ao contrato em
/// <c>specs/001-api-cadastro-clientes-pedidos/contracts/openapi.yaml</c> (T094) em dois pontos:
/// <list type="number">
/// <item>os nomes dos parâmetros de <c>[FromQuery]</c> saem em PascalCase (<c>PageSize</c>) porque
/// a ApiExplorer usa o nome da propriedade C#; o contrato, a API real e os erros de validação usam
/// camelCase, e</item>
/// <item>a autenticação é declarada <b>por operação</b> em vez de global. As rotas
/// <c>[AllowAnonymous]</c> (<c>/auth/*</c>) ficam sem requisito nenhum — no contrato elas têm
/// <c>security: []</c>. Um requisito global não pode ser "desligado" por operação porque a
/// Microsoft.OpenApi não serializa a lista vazia: <c>security</c> ausente num documento sem
/// requisito global é exatamente "não precisa de token".</item>
/// </list>
/// O botão Authorize do Swagger UI continua funcionando: o esquema <c>Bearer</c> é definido em
/// <c>Program.cs</c> e passa a ser referenciado nas operações protegidas.
/// </summary>
public class ContractAlignmentOperationFilter : IOperationFilter
{
    /// <summary>Id do esquema de segurança declarado em <c>Program.cs</c> (AddSecurityDefinition).</summary>
    private const string BearerSchemeId = "Bearer";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        RenameQueryParameters(operation);
        ApplySecurityByAuthenticationMetadata(operation, context);
    }

    private static void RenameQueryParameters(OpenApiOperation operation)
    {
        foreach (var parameter in operation.Parameters)
        {
            if (parameter.In != ParameterLocation.Query)
            {
                continue;
            }

            var camel = OpenApiNaming.ToCamelCase(parameter.Name);

            // O binding real aceita as duas formas (a ModelBinder de query é case-insensitive); o
            // documento passa a mostrar a forma do contrato.
            if (camel.Length > 0 && camel != parameter.Name)
            {
                parameter.Name = camel;
            }
        }
    }

    private static void ApplySecurityByAuthenticationMetadata(
        OpenApiOperation operation, OperationFilterContext context)
    {
        if (IsAnonymous(context))
        {
            // Requisito global inexistente + security ausente = rota pública, igual a
            // `security: []` do contrato.
            operation.Security = null;
            return;
        }

        operation.Security =
        [
            new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = BearerSchemeId
                        }
                    },
                    Array.Empty<string>()
                }
            }
        ];
    }

    /// <summary>
    /// <c>[AllowAnonymous]</c> na ação ou na classe (AuthController) marca a operação como pública;
    /// a regra vem do mesmo atributo que o pipeline de autorização usa, não de uma lista de caminhos
    /// mantida à mão.
    /// </summary>
    private static bool IsAnonymous(OperationFilterContext context) =>
        context.MethodInfo.GetCustomAttributes(inherit: true).OfType<IAllowAnonymous>().Any()
        || context.MethodInfo.DeclaringType?
            .GetCustomAttributes(inherit: true)
            .OfType<IAllowAnonymous>()
            .Any() == true;
}
