using Ambrosia.Domain.Exceptions;
using Ambrosia.API.Swagger;
using Ambrosia.DTO.Common;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Ambrosia.API.Controllers;

/// <summary>
/// Base dos controllers de dados: concentra o mapeamento exceção → HTTP no formato
/// <see cref="ErrorResult"/> exigido pela FR-024 e pela constituição §IV
/// (chaves <c>sucesso</c>, <c>mensagem</c>, <c>erros</c>, mensagens em português citando o campo).
/// Cada controller mantém seu próprio <c>try/catch</c> explícito (constituição — Restrições
/// Adicionais / Tratamento de Erros).
/// </summary>
[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>Mensagem geral das respostas 400.</summary>
    private const string InvalidDataMessage = "Dados inválidos.";

    /// <summary>
    /// HTTP 400 com o detalhamento por campo de um <see cref="ValidationException"/>
    /// (FluentValidation). <c>PropertyName</c> é convertido para camelCase por segmento:
    /// <c>Items[0].ImageUrl</c> → <c>items[0].imageUrl</c>.
    /// </summary>
    protected IActionResult ValidationError(ValidationException ex)
    {
        var erros = ex.Errors
            .Select(failure => new FieldError
            {
                Campo = ToCamelCaseField(failure.PropertyName),
                Mensagem = failure.ErrorMessage
            })
            .ToList();

        return BadRequest(new ErrorResult
        {
            Sucesso = false,
            Mensagem = InvalidDataMessage,
            Erros = erros
        });
    }

    /// <summary>HTTP 404 (recurso inexistente ou de outro usuário — nunca distingue os dois).</summary>
    protected IActionResult NotFoundError(NotFoundException ex)
    {
        return NotFound(new ErrorResult
        {
            Sucesso = false,
            Mensagem = ex.Message,
            Erros = []
        });
    }

    /// <summary>HTTP 409 (violação de unicidade: CPF ou pedido duplicado).</summary>
    protected IActionResult ConflictError(DuplicateException ex)
    {
        return Conflict(new ErrorResult
        {
            Sucesso = false,
            Mensagem = ex.Message,
            Erros = []
        });
    }

    /// <summary>
    /// HTTP 400 no formato <see cref="ErrorResult"/> para falhas de binding/desserialização.
    /// Necessário porque <c>SuppressModelStateInvalidFilter = true</c> (T025) desliga o
    /// ProblemDetails automático do <c>[ApiController]</c>.
    /// </summary>
    protected IActionResult ValidationError(ModelStateDictionary modelState)
    {
        var erros = modelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .SelectMany(entry => entry.Value!.Errors
                .Select(error => new FieldError
                {
                    Campo = ToCamelCaseField(entry.Key.Length == 0 ? "body" : entry.Key),
                    Mensagem = string.IsNullOrWhiteSpace(error.ErrorMessage)
                        ? "Valor inválido."
                        : error.ErrorMessage
                }))
            .ToList();

        return BadRequest(new ErrorResult
        {
            Sucesso = false,
            Mensagem = InvalidDataMessage,
            Erros = erros
        });
    }

    /// <summary>
    /// Retorna um 400 <see cref="ErrorResult"/> quando o binding do corpo falhou; <c>null</c>
    /// quando o estado é válido. Use no início das ações de criação/atualização:
    /// <c>if (ModelStateError() is { } badRequest) return badRequest;</c>
    /// </summary>
    protected IActionResult? ModelStateError()
    {
        return ModelState.IsValid ? null : ValidationError(ModelState);
    }

    /// <summary>
    /// Converte um caminho de propriedade FluentValidation em camelCase por segmento,
    /// preservando índices: <c>Items[0].ImageUrl</c> → <c>items[0].imageUrl</c>.
    /// O camelCase de cada nome é o mesmo usado pela documentação OpenAPI
    /// (<see cref="OpenApiNaming.ToCamelCase"/>), para que campo citado no erro, parâmetro de
    /// query e contrato nunca divirjam.
    /// </summary>
    internal static string ToCamelCaseField(string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return string.Empty;
        }

        var segments = propertyName.Split('.');
        var builder = new System.Text.StringBuilder();

        for (var i = 0; i < segments.Length; i++)
        {
            if (i > 0)
            {
                builder.Append('.');
            }

            builder.Append(LowerFirstSegment(segments[i]));
        }

        return builder.ToString();

        static string LowerFirstSegment(string segment)
        {
            var bracket = segment.IndexOf('[');

            // O índice (entre colchetes) é preservado tal como veio.
            return bracket < 0
                ? OpenApiNaming.ToCamelCase(segment)
                : OpenApiNaming.ToCamelCase(segment[..bracket]) + segment[bracket..];
        }
    }
}
