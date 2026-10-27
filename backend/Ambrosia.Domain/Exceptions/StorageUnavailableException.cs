namespace Ambrosia.Domain.Exceptions;

/// <summary>
/// Lançada pela Infra quando o storage de objetos está inalcançável ou recusou a operação por um
/// problema de serviço (pesquisa §11): o controller a converte em HTTP <b>503</b> com a mensagem fixa
/// de <see cref="MESSAGE"/>, sem detalhe nenhum.
/// <para>
/// A exceção original vai apenas como <see cref="Exception.InnerException"/> e é logada pelo
/// <c>catch (Exception)</c> do controller — nunca no <see cref="Exception.Message"/> — porque o
/// <c>Message</c> do SDK inclui endpoint, bucket e chave do objeto, e os erros 500 da API devolvem
/// <c>ex.Message</c> ao cliente. Vazar isso entregaria a estrutura do storage (FR-021).
/// </para>
/// </summary>
public class StorageUnavailableException : Exception
{
    /// <summary>Mensagem pública, única e fixa (contrato <c>images-api.md</c>).</summary>
    public const string MESSAGE = "Armazenamento de imagens indisponível.";

    public StorageUnavailableException() : base(MESSAGE)
    {
    }

    public StorageUnavailableException(Exception inner) : base(MESSAGE, inner)
    {
    }
}
