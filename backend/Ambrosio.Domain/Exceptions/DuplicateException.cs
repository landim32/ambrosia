namespace Ambrosio.Domain.Exceptions;

/// <summary>
/// Lançada em violação de unicidade (CPF repetido por usuário, pedido repetido por
/// número/data/tipo). O controller a converte em HTTP 409 (FR-010, FR-014).
/// </summary>
public class DuplicateException : Exception
{
    public DuplicateException(string message) : base(message)
    {
    }

    public DuplicateException() : base("Já existe um registro com estes dados.")
    {
    }
}
