namespace Ambrosia.Domain.Exceptions;

/// <summary>
/// A vendedora pediu o cancelamento de uma execução que já terminou (feature 006, FR-019). O
/// controller a converte em HTTP <c>409</c>: não há mais o que cancelar, e o histórico fica intacto.
/// </summary>
public class ImportRunNotCancellableException : Exception
{
    public ImportRunNotCancellableException()
        : base("A execução já foi finalizada.")
    {
    }
}
