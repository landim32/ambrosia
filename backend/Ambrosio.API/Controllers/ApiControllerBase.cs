using System.Globalization;
using Ambrosio.Domain.Common;
using Ambrosio.Domain.Exceptions;
using Ambrosio.Domain.Interfaces;
using Ambrosio.API.Swagger;
using Ambrosio.DTO.Common;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Ambrosio.API.Controllers;

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
    protected const string InvalidDataMessage = "Dados inválidos.";

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
    /// HTTP 401 (FR-008): credencial recusada no login com e-mail e senha. A mensagem vem
    /// pronta na exceção e é sempre a mesma — nunca revela se o e-mail existe, se a senha está
    /// errada ou se a conta não tem senha. <c>erros</c> vazio: não é falha de formato de campo.
    /// </summary>
    protected IActionResult InvalidCredentialsError(InvalidCredentialsException ex)
    {
        return Unauthorized(new ErrorResult
        {
            Sucesso = false,
            Mensagem = ex.Message,
            Erros = []
        });
    }

    /// <summary>
    /// Relógio da requisição (o <see cref="IClock"/> do Domain, resolvido do contêiner), para que o
    /// <c>Retry-After</c> use o "agora" injetável em vez de um relógio estático.
    /// </summary>
    protected IClock Clock =>
        HttpContext.RequestServices.GetService(typeof(IClock)) as IClock ?? new SystemClock();

    /// <summary>
    /// HTTP 429 (feature 006, FR-008a): novos inícios de importação bloqueados por recusas seguidas de
    /// credencial do +Vendas. O cabeçalho <c>Retry-After</c> leva <b>só os segundos</b> — nunca o
    /// horário absoluto, que permitiria sondar o fuso e o relógio do servidor — e a mensagem traz o
    /// horário local da vendedora, porque é o que ela pode conferir no pulso.
    /// <para>
    /// Nem o login nem a senha aparecem aqui: a mensagem é uma constante com o horário (FR-006).
    /// </para>
    /// </summary>
    protected IActionResult ImportLockedError(ImportLockedException ex)
    {
        var retryAfterSeconds = (long)Math.Max(1, Math.Ceiling((ex.LockedUntil - Clock.UtcNow).TotalSeconds));
        var localRelease = SaoPauloCalendar.ToSaoPaulo(ex.LockedUntil);

        Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

        return StatusCode(StatusCodes.Status429TooManyRequests, new ErrorResult
        {
            Sucesso = false,
            Mensagem = $"Muitas tentativas com credenciais recusadas. Tente novamente às {localRelease:HH:mm}.",
            Erros = []
        });
    }

    /// <summary>
    /// HTTP 409 (feature 006, FR-019): cancelamento pedido numa execução que já terminou. Sem corpo de
    /// detalhe além da mensagem — <c>erros</c> vazio porque não é falha de campo.
    /// </summary>
    protected IActionResult NotCancellableError(ImportRunNotCancellableException ex)
    {
        return Conflict(new ErrorResult
        {
            Sucesso = false,
            Mensagem = ex.Message,
            Erros = []
        });
    }

    /// <summary>
    /// HTTP 400 (feature 004): a confirmação de envio descobriu que o binário no storage não confere
    /// com o declarado — tamanho, tipo ou hash — ou que o objeto ainda não foi enviado, ou que o envio
    /// já tinha sido recusado. A mensagem vem pronta na exceção, exatamente como no contrato
    /// <c>specs/004-s3-image-storage/contracts/images-api.md</c>. <c>erros</c> vazio: não é falha de
    /// formato de campo, e o motivo já está na mensagem.
    /// </summary>
    protected IActionResult ImageRejectedError(ImageRejectedException ex)
    {
        return BadRequest(new ErrorResult
        {
            Sucesso = false,
            Mensagem = ex.Message,
            Erros = []
        });
    }

    /// <summary>
    /// HTTP 503 (FR-021): o storage de imagens está inalcançável ou recusou a operação. A mensagem é a
    /// constante fixa da exceção — nunca o <c>Message</c> do SDK, que traria endpoint, bucket e chave do
    /// objeto para dentro da resposta. <c>erros</c> vazio pelo mesmo motivo do 401.
    /// </summary>
    protected IActionResult StorageUnavailableError(StorageUnavailableException ex)
    {
        return StatusCode(StatusCodes.Status503ServiceUnavailable, new ErrorResult
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
