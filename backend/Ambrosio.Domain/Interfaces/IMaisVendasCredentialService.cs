using Ambrosio.DTO.Imports;
using Ambrosio.DTO.Imports.Collector;

namespace Ambrosio.Domain.Interfaces;

/// <summary>
/// Credenciais do +Vendas da vendedora (feature 008, US1/US2, data-model §7): cadastrar, consultar o
/// estado, remover e — só no instante de uso — decifrar para o login na origem.
/// <para>
/// É o único caminho entre a cifra da Infra e o resto do sistema. Nenhum método devolve a senha, e
/// nenhum grava o que recebeu em claro: a cifra acontece aqui, na transação da gravação (FR-003).
/// </para>
/// </summary>
public interface IMaisVendasCredentialService
{
    /// <summary>
    /// Mensagem única de "sem credenciais" — a frase exata do contrato (<c>contracts/api.md</c>,
    /// research R4; repetida em <c>tasks.md</c> T020), usada pelo <c>POST /runs</c>, pelo
    /// <c>POST /runs/update-base</c> e, no plugin, como texto-base do erro que ganha o link da tela.
    /// </summary>
    public const string NOT_SAVED_MESSAGE = "Cadastre as credenciais do +Vendas na configuração.";

    /// <summary>Estado para a tela de configuração: login em claro, datas e situação; nunca a senha.</summary>
    Task<MaisVendasCredentialsInfo> GetAsync(long userId);

    /// <summary>
    /// Cadastro ou troca (0 → 1, 2 → 1). Regras de presença dependem do que já está gravado: sem
    /// cadastro, login e senha vêm juntos; com cadastro, troco só a senha, e trocar o login exige a
    /// senha porque o +Vendas só autentica os dois juntos.
    /// </summary>
    /// <exception cref="FluentValidation.ValidationException">Combinação recusada (400), no campo contratado.</exception>
    Task<MaisVendasCredentialsInfo> SaveAsync(long userId, MaisVendasCredentialsUpdateInfo info);

    /// <summary>Remove as credenciais (1 ou 2 → 0). Idempotente: sem cadastro, é 200 sem writes.</summary>
    Task<MaisVendasCredentialsInfo> DeleteAsync(long userId);

    /// <summary>Há as duas credenciais cifradas? Não lê nem decifra nada (é a checagem do início).</summary>
    Task<bool> HasCredentialsAsync(long userId);

    /// <summary>
    /// Login e senha em claro para <b>uma</b> autenticação no +Vendas (research R3). Chamado só pelo
    /// executor, no instante do login na origem; o resultado vive enquanto dura a sessão de coleta e
    /// não é guardado em lugar nenhum.
    /// </summary>
    /// <exception cref="FluentValidation.ValidationException">Sem credenciais (400 / <c>isError</c> no plugin).</exception>
    Task<MaisVendasCredentials> GetDecryptedAsync(long userId);
}
