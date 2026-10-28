using Ambrosio.Domain.Interfaces;
using Ambrosio.DTO.Enums;
using Ambrosio.McpServer.Mcp.Results;
using FluentValidation;
using ModelContextProtocol.Protocol;

namespace Ambrosio.McpServer.Mcp;

/// <summary>
/// Padrão <c>confirmar</c> das tools de escrita (feature 007, T026, R11).
/// </summary>
/// <remarks>
/// Com <c>confirmar: false</c> roda só a validação (validators FluentValidation e as checagens de
/// existência e duplicidade que a tool passar) e devolve a prévia, sem gravar nem auditar. Com
/// <c>confirmar: true</c> executa a mesma chamada de domínio do controller e audita o desfecho:
/// gravado, recusado por regra de negócio ou erro inesperado.
/// </remarks>
public sealed class WriteToolHelper(
    McpUserContext user,
    IMcpAuditService audit,
    IServiceProvider services,
    ILogger<WriteToolHelper> logger)
{
    public const string PreviewNotice = "Nada foi gravado. Confirme para gravar.";

    /// <summary>Roda o validator registrado para o DTO e lança <see cref="ValidationException"/> se falhar.</summary>
    public async Task ValidateAsync<T>(T info)
    {
        if (services.GetService<IValidator<T>>() is { } validator)
        {
            await validator.ValidateAndThrowAsync(info);
        }
    }

    /// <param name="confirmar">Argumento da tool.</param>
    /// <param name="preview">Validação + texto da prévia; não pode gravar nada.</param>
    /// <param name="execute">A gravação; devolve o objeto gravado e o texto do resultado.</param>
    public async Task<CallToolResult> RunAsync(
        bool confirmar,
        Func<Task<(object Preview, string Text)>> preview,
        Func<Task<(object Saved, string Text)>> execute)
    {
        if (!confirmar)
        {
            try
            {
                var (previewObject, text) = await preview();

                return ToolResultFactory.Success(
                    new { previa = previewObject, gravado = false, aviso = PreviewNotice },
                    $"{text} {PreviewNotice}");
            }
            catch (Exception ex) when (ToolResultFactory.DomainMessage(ex) is not null)
            {
                return ToolResultFactory.FromDomainException(ex)!;
            }
        }

        var userId = user.RequireUserId();

        try
        {
            var (saved, text) = await execute();
            await audit.RecordAsync(userId, user.ToolName, user.Arguments, McpAuditOutcome.Recorded, user.ClientId, text);

            return ToolResultFactory.Success(new { registro = saved, gravado = true }, text);
        }
        catch (Exception ex) when (ToolResultFactory.DomainMessage(ex) is { } message)
        {
            await audit.RecordAsync(userId, user.ToolName, user.Arguments, McpAuditOutcome.RejectedByRule, user.ClientId, message);

            return ToolResultFactory.Error(message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha inesperada na tool {Tool} do usuário {UserId}.", user.ToolName, userId);
            await audit.RecordAsync(
                userId, user.ToolName, user.Arguments, McpAuditOutcome.UnexpectedError, user.ClientId, ex.GetType().Name);

            return ToolResultFactory.Error(ToolResultFactory.GenericErrorMessage);
        }
    }
}
