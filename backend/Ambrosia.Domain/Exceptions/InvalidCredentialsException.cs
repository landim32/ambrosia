namespace Ambrosia.Domain.Exceptions;

/// <summary>
/// Credencial recusada no login com e-mail e senha (FR-008). A mensagem é única e
/// <b>nunca</b> distingue e-mail inexistente, senha errada ou conta sem senha, para não
/// revelar quais e-mails existem no cadastro. O controller a converte em HTTP 401
/// (<c>contracts/auth-api.md</c>).
/// </summary>
public class InvalidCredentialsException : Exception
{
    /// <summary>Mensagem fixa do 401 — única, genérica, sem pontuar o motivo real.</summary>
    public const string MESSAGE = "E-mail ou senha inválidos";

    public InvalidCredentialsException() : base(MESSAGE)
    {
    }
}
