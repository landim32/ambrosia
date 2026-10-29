using System.Security.Cryptography;
using Ambrosio.Domain.Interfaces;
using Ambrosio.Domain.Models;
using Ambrosio.DTO.Enums;
using Ambrosio.DTO.Imports;
using Ambrosio.DTO.Imports.Collector;
using Ambrosio.Infra.Interfaces.AppServices;
using Ambrosio.Infra.Interfaces.Repository;
using FluentValidation;
using FluentValidation.Results;

namespace Ambrosio.Domain.Services;

/// <summary>
/// Cadastro, consulta e remoção das credenciais do +Vendas, e a decifração no instante de uso
/// (feature 008, US1/US2, data-model §7, research R2/R3).
/// <para>
/// <b>O que este serviço garante</b>: a senha em claro existe dentro de uma chamada de método e morre
/// com ela — cifrada pela Infra antes de qualquer gravação, e nunca copiada para um campo de DTO de
/// saída, para o log ou para uma exceção. O login é o único segredo de terceiro que volta para a
/// própria vendedora, porque ela precisa dele para editar o cadastro (FR-001/FR-003).
/// </para>
/// <para>
/// As checagens de presença dependem do que já está gravado, e por isso não dá para jogar tudo num
/// validator: <c>null</c> no JSON significa "não mexi nesse campo", e o que muda de comportamento é
/// justamente se a vendedora já tem cadastro (contrato da API: sem cadastro, login e senha vêm
/// juntos; com cadastro, a senha pode ser trocada sozinha).
/// </para>
/// </summary>
public class MaisVendasCredentialService : IMaisVendasCredentialService
{
    private readonly IMaisVendasAccountRepository<MaisVendasAccount> _accounts;
    private readonly IMaisVendasCredentialCipherAppService _cipher;
    private readonly IValidator<MaisVendasCredentialsUpdateInfo> _validator;
    private readonly IClock _clock;

    public MaisVendasCredentialService(
        IMaisVendasAccountRepository<MaisVendasAccount> accounts,
        IMaisVendasCredentialCipherAppService cipher,
        IValidator<MaisVendasCredentialsUpdateInfo> validator,
        IClock clock)
    {
        _accounts = accounts;
        _cipher = cipher;
        _validator = validator;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<MaisVendasCredentialsInfo> GetAsync(long userId)
    {
        var account = await _accounts.GetOrCreateAsync(userId, _clock.UtcNow);

        return Map(account);
    }

    /// <inheritdoc />
    public async Task<MaisVendasCredentialsInfo> SaveAsync(long userId, MaisVendasCredentialsUpdateInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        // Formato primeiro (tamanhos e "campo enviado não pode ser vazio"), presença depois: as duas
        // checagens de combinação dependem do que já está gravado e por isso não cabem no validator.
        await _validator.ValidateAndThrowAsync(info);

        var now = _clock.UtcNow;
        var account = await _accounts.GetOrCreateAsync(userId, now);

        // O login é aparado antes de cifrar; a senha NUNCA é. Um espaço perdido no fim do login
        // derrubaria o login no +Vendas com "credenciais recusadas" sem nenhuma causa visível, e
        // espaço em branco não faz parte de nome de conta. Na senha é o contrário: ela pode ter
        // espaço, e aparar seria alterar um segredo que a vendedora digitou.
        var incomingLogin = info.Login?.Trim();
        var hasPassword = !string.IsNullOrWhiteSpace(info.Password);

        if (!account.HasCredentials)
        {
            return await SaveFirstTimeAsync(account, userId, incomingLogin, info, now);
        }

        // Troca de senha com o login já guardado: reuso o login cifrado que está na linha — decifrar
        // para recifrar o mesmo valor não acrescenta nada e ampliaria o alcance da senha em claro.
        if (!hasPassword)
        {
            var storedLogin = TryDecryptLogin(account, userId);

            if (string.IsNullOrEmpty(incomingLogin) || SameLogin(incomingLogin, storedLogin))
            {
                // Nada para mudar (login igual ou ausente, senha ausente): devolve o estado atual sem
                // gravar — o PUT é idempotente por contrato.
                return Map(account);
            }

            throw new ValidationException([
                new ValidationFailure("password", "Informe a senha para trocar o login.")]);
        }

        if (string.IsNullOrEmpty(incomingLogin))
        {
            var existingLoginCipher = account.LoginEncrypted!;
            var newPasswordCipher = Cipher(userId, IMaisVendasCredentialCipherAppService.FIELD_PASSWORD, info.Password!);

            // Senha nova com o login que já está guardado: a cifra do login é copiada como está —
            // decifrá-la para recifrar o mesmo valor só ampliaria o alcance da senha em claro.
            account.SetCredentials(existingLoginCipher, newPasswordCipher, now);
        }
        else
        {
            account.SetCredentials(
                Cipher(userId, IMaisVendasCredentialCipherAppService.FIELD_LOGIN, incomingLogin),
                Cipher(userId, IMaisVendasCredentialCipherAppService.FIELD_PASSWORD, info.Password!),
                now);
        }

        await _accounts.UpdateAsync(account);

        return Map(account);
    }

    /// <inheritdoc />
    public async Task<MaisVendasCredentialsInfo> DeleteAsync(long userId)
    {
        var now = _clock.UtcNow;
        var account = await _accounts.GetOrCreateAsync(userId, now);

        if (!account.HasCredentials)
        {
            // Idempotência pedida pelo contrato (DELETE sem cadastro é 200) sem escrever: uma linha
            // zerada gravada de novo só moveria updated_at à toa.
            return Map(account);
        }

        account.ClearCredentials(now);

        await _accounts.UpdateAsync(account);

        return Map(account);
    }

    /// <inheritdoc />
    public async Task<MaisVendasCredentials> GetDecryptedAsync(long userId)
    {
        var account = await _accounts.GetOrCreateAsync(userId, _clock.UtcNow);

        if (!account.HasCredentials)
        {
            throw new ValidationException([
                new ValidationFailure("login", IMaisVendasCredentialService.NOT_SAVED_MESSAGE)]);
        }

        // Decifração no instante do uso (research R3): os dois valores voltam juntos porque o +Vendas
        // só autentica o par, e o chamador (executor) descarta a referência no finally do job.
        var login = _cipher.Decrypt(userId, IMaisVendasCredentialCipherAppService.FIELD_LOGIN, account.LoginEncrypted!);
        var password = _cipher.Decrypt(userId, IMaisVendasCredentialCipherAppService.FIELD_PASSWORD, account.PasswordEncrypted!);

        return new MaisVendasCredentials(login, password);
    }

    /// <inheritdoc />
    public async Task<bool> HasCredentialsAsync(long userId)
    {
        // Só olha as colunas: uma conta pode ter as duas cifras preenchidas sem que o login seja
        // decifrável, e o que o início precisa saber é "existe cadastro", não "existe senha válida".
        var account = await _accounts.GetOrCreateAsync(userId, _clock.UtcNow);

        return account.HasCredentials;
    }

    // ------------------------------------------------------------------------------------ gravação

    /// <summary>
    /// Primeiro cadastro (0 → 1): login e senha juntos, porque guarda-los separados não deixaria a
    /// vendedora importar nada.
    /// </summary>
    private async Task<MaisVendasCredentialsInfo> SaveFirstTimeAsync(
        MaisVendasAccount account,
        long userId,
        string? incomingLogin,
        MaisVendasCredentialsUpdateInfo info,
        DateTime now)
    {
        if (string.IsNullOrEmpty(incomingLogin))
        {
            throw new ValidationException([new ValidationFailure("login", "Informe o login do +Vendas.")]);
        }

        if (string.IsNullOrWhiteSpace(info.Password))
        {
            throw new ValidationException([
                new ValidationFailure("password", "Informe a senha junto com o login.")]);
        }

        account.SetCredentials(
            Cipher(userId, IMaisVendasCredentialCipherAppService.FIELD_LOGIN, incomingLogin),
            Cipher(userId, IMaisVendasCredentialCipherAppService.FIELD_PASSWORD, info.Password),
            now);

        await _accounts.UpdateAsync(account);

        return Map(account);
    }

    // ------------------------------------------------------------------------------------ leitura

    private string Cipher(long userId, string field, string plain) => _cipher.Encrypt(userId, field, plain);

    /// <summary>
    /// Login em claro para comparação e para a tela. Uma cifra que não decifra (chave trocada, payload
    /// corrompido) é tratada como "não conheço o login", nunca como erro da requisição: a vendedora
    /// ainda pode salvar um cadastro novo por cima, que é a saída do problema.
    /// </summary>
    private string? TryDecryptLogin(MaisVendasAccount account, long userId)
    {
        try
        {
            return _cipher.Decrypt(userId, IMaisVendasCredentialCipherAppService.FIELD_LOGIN, account.LoginEncrypted!);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>
    /// Comparação exata, sem normalizar: o +Vendas diferencia <c>Conta+S1</c> de <c>conta+s1</c>, e
    /// achar que é o mesmo login faria a troca silenciosa de conta (FR-008b da 006) passar sem senha.
    /// </summary>
    private static bool SameLogin(string incoming, string? stored) =>
        stored is not null && string.Equals(incoming, stored, StringComparison.Ordinal);

    /// <summary>
    /// Estado para a tela. A senha não entra nem mascarada — <c>hasPassword</c> diz que ela existe, e
    /// é o máximo que qualquer superfície do sistema sabe sobre ela (FR-003, SC-003).
    /// </summary>
    private MaisVendasCredentialsInfo Map(MaisVendasAccount account)
    {
        var login = account.HasCredentials ? TryDecryptLogin(account, account.UserId) : null;

        return new MaisVendasCredentialsInfo
        {
            Login = login ?? string.Empty,
            HasPassword = account.HasCredentials,
            SavedAt = account.CredentialsSavedAt,
            PasswordChangedAt = account.PasswordChangedAt,
            Status = account.HasCredentials ? account.CredentialsStatus : MaisVendasCredentialsStatus.None
        };
    }
}
