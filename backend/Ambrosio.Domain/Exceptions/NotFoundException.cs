namespace Ambrosio.Domain.Exceptions;

/// <summary>
/// Lançada quando um recurso não existe <b>ou pertence a outro usuário</b> (FR-022). O controller
/// a converte em HTTP 404 com a mesma mensagem nos dois casos, para não revelar a existência
/// do recurso (SC-002).
/// </summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message)
    {
    }

    public NotFoundException() : base("Recurso não encontrado.")
    {
    }
}
