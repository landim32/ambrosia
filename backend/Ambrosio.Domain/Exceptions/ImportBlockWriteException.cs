namespace Ambrosio.Domain.Exceptions;

/// <summary>
/// A gravação de um pedido importado falhou por motivo inesperado (feature 006, FR-012/FR-013): o
/// conteúdo estoura uma coluna, a FK recusou, o banco caiu no meio da transação daquele pedido.
/// <para>
/// É o caso que faz o <b>bloco</b> falhar — diferente de "pedido com dado inválido", que conta
/// <c>error</c> e deixa o bloco continuar (FR-036). O que já foi gravado do bloco fica, a execução
/// para, e os blocos seguintes viram <c>notExecuted</c>: é o cenário <c>falha-bloco3</c> (14 pedidos
/// gravados e o 15º estourando <c>order_number varchar(60)</c>).
/// </summary>
public class ImportBlockWriteException : Exception
{
    /// <summary>Mensagem única e segura para o banco/a tela (FR-052).</summary>
    public const string MESSAGE = "Falha inesperada ao gravar o pedido na base.";

    public ImportBlockWriteException(string? detail = null) : base(MESSAGE) =>
        Detail = detail;

    /// <summary>
    /// Texto técnico do motivo, para o log estruturado do worker. Não é a mensagem da exceção: nunca
    /// vai para <c>error_message</c> nem para uma resposta HTTP.
    /// </summary>
    public string? Detail { get; }
}

/// <summary>
/// O banco recusou uma escrita de pedido/cliente na importação (feature 006). Envolve o erro real para
/// que <see cref="ImportBlockWriteException"/> decida entre "conflito" e "bloco falho" sem comparar
/// texto de mensagem de driver.
/// </summary>
public class ImportWriteFailedException : Exception
{
    public ImportWriteFailedException(string detail, Exception inner) : base(ImportBlockWriteException.MESSAGE, inner) =>
        Detail = detail;

    /// <summary>Motivo técnico (coluna, FK, timeout). Log apenas.</summary>
    public string Detail { get; }
}
