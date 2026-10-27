namespace Ambrosio.Domain.Exceptions;

/// <summary>
/// A origem (<c>+Vendas</c>) ficou indisponível depois das retentativas do coletor (feature 006,
/// FR-045): timeout, "Erro ao carregar…" ou resposta não-2xx três vezes seguidas. O bloco falha e a
/// execução para, com o que já foi gravado preservado.
/// <para>
/// A mensagem é sempre a mesma frase fixa — nunca contém login, senha, CPF, cabeçalho, cookie ou URL
/// assinada (FR-052). Ela termina em <c>import_run_blocks.error_message</c>, que é visível na tela, e
/// o <c>Exception</c> não tratado do pipeline é devolvido como <c>500</c> com <c>ex.Message</c>.
/// </para>
/// </summary>
public class MaisVendasSourceException : Exception
{
    /// <summary>Texto único do contrato.</summary>
    public const string MESSAGE = "O +Vendas não respondeu às tentativas de coleta.";

    public MaisVendasSourceException() : base(MESSAGE)
    {
    }

    /// <summary>
    /// <paramref name="detail"/> é o motivo técnico vindo do coletor (timeout, seletor ausente,
    /// resposta 503). Ele não vai para a mensagem — fica em <see cref="Detail"/>, onde o worker pode
    /// registrá-lo no log estruturado com rota e tempo. O coletor continua obrigado a escrever o
    /// detalhe sem credencial nem dado pessoal (FR-052).
    /// </summary>
    public MaisVendasSourceException(string? detail) : base(MESSAGE) => Detail = detail;

    /// <summary>Origem do erro de infraestrutura, preservada para o log.</summary>
    public MaisVendasSourceException(string? detail, Exception inner) : base(MESSAGE, inner) => Detail = detail;

    /// <summary>Motivo técnico opcional, para o log. Nunca é a mensagem da exceção.</summary>
    public string? Detail { get; }
}
