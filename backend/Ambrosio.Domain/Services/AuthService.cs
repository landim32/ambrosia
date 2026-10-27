using Ambrosio.Domain.Exceptions;
using Ambrosio.Domain.Interfaces;
using Ambrosio.Domain.Models;
using Ambrosio.DTO.Auth;
using Ambrosio.DTO.Enums;
using Ambrosio.DTO.Users;
using Ambrosio.Infra.Interfaces.AppServices;
using Ambrosio.Infra.Interfaces.Repository;
using FluentValidation;
using FluentValidation.Results;

namespace Ambrosio.Domain.Services;

/// <summary>
/// Regras de cadastro e login da feature (FR-001, FR-002, FR-021): valida o DTO de entrada,
/// resolve o provedor externo pela abstração da Infra, cria ou reutiliza o usuário pelo e-mail
/// normalizado e emite o JWT do Ambrosio com <c>sub = user_id</c>. A senha só existe como hash
/// (via <see cref="IPasswordHashAppService"/>) e nunca sai daqui para um DTO (FR-005).
/// </summary>
public class AuthService : IAuthService
{
    /// <summary>Campo citado nos erros de validação do fluxo Google (FR-024).</summary>
    private const string ID_TOKEN_FIELD = "idToken";

    /// <summary>409 genérico: não revela quem criou a conta nem por qual provedor (FR-006).</summary>
    private const string EMAIL_TAKEN = "Este e-mail já está cadastrado.";

    /// <summary>409 de conta só-Google: orienta o caminho que realmente entra (clarificação Q2).</summary>
    private const string EMAIL_TAKEN_BY_GOOGLE =
        "Este e-mail já está cadastrado. Entre com sua conta Google.";

    private readonly IUserRepository<User> _users;
    private readonly IOAuthTokenAppService _oauthTokenAppService;
    private readonly IJwtTokenAppService _jwtTokenAppService;
    private readonly IPasswordHashAppService _passwordHash;
    private readonly IValidator<GoogleLoginInfo> _googleLoginValidator;
    private readonly IValidator<RegisterInfo> _registerValidator;
    private readonly IValidator<LoginInfo> _loginValidator;
    private readonly IClock _clock;

    public AuthService(
        IUserRepository<User> users,
        IOAuthTokenAppService oauthTokenAppService,
        IJwtTokenAppService jwtTokenAppService,
        IPasswordHashAppService passwordHash,
        IValidator<GoogleLoginInfo> googleLoginValidator,
        IValidator<RegisterInfo> registerValidator,
        IValidator<LoginInfo> loginValidator,
        IClock clock)
    {
        _users = users;
        _oauthTokenAppService = oauthTokenAppService;
        _jwtTokenAppService = jwtTokenAppService;
        _passwordHash = passwordHash;
        _googleLoginValidator = googleLoginValidator;
        _registerValidator = registerValidator;
        _loginValidator = loginValidator;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<LoginResult> RegisterAsync(RegisterInfo info)
    {
        await _registerValidator.ValidateAndThrowAsync(info);

        var email = User.NormalizeEmail(info.Email);
        var name = info.Name.Trim();

        // Senha SEM trim (edge case da spec): espaços nas pontas fazem parte da senha, e o mesmo
        // valor tem de valer no login. O que vai para o banco é o hash, nunca a senha (FR-005).
        var existing = await _users.GetByEmailAsync(email);

        if (existing is not null)
        {
            // Nunca define senha em conta existente (FR-006 / clarificação Q2): sem verificação
            // de e-mail, isso seria sequestro de conta pré-criada.
            throw new DuplicateException(
                existing.Provider == AuthProvider.Google ? EMAIL_TAKEN_BY_GOOGLE : EMAIL_TAKEN);
        }

        var now = _clock.UtcNow;
        var created = new User
        {
            Email = email,
            Name = name,
            Provider = AuthProvider.Password,
            ProviderSubject = null,
            CreatedAt = now,
            UpdatedAt = now
        };

        created.SetPassword(_passwordHash.Hash(info.Password), now);

        // Diferente do upsert do login, a corrida de cadastros simultâneos NÃO é reabsorvida: o
        // índice único ux_users_email decide quem venceu e a segunda requisição devolve 409 com a
        // mesma mensagem (research §4). Só uma conta é criada.
        return BuildLoginResult(await _users.InsertAsync(created));
    }

    /// <inheritdoc />
    public async Task<LoginResult> LoginWithPasswordAsync(LoginInfo info)
    {
        await ThrowIfInvalid(_loginValidator, info);

        var email = User.NormalizeEmail(info.Email);
        var user = await _users.GetByEmailAsync(email);

        // SEMPRE exatamente uma derivação PBKDF2 por requisição (research §2): quando o usuário
        // não existe ou não tem senha, a senha informada é conferida contra um hash fictício.
        // O custo fica igual ao do login válido, então o tempo de resposta não revela quais
        // e-mails estão cadastrados (FR-008, SC-006).
        if (user is null || !user.HasPassword)
        {
            _ = _passwordHash.Verify(info.Password, _passwordHash.DummyHash);

            throw new InvalidCredentialsException();
        }

        if (!_passwordHash.Verify(info.Password, user.PasswordHash))
        {
            throw new InvalidCredentialsException();
        }

        // A senha não vai para log nem para exceção: só o instante de uso é registrado.
        user.Touch(_clock.UtcNow);

        return BuildLoginResult(await _users.UpdateAsync(user));
    }

    /// <inheritdoc />
    public async Task<LoginResult> LoginWithGoogleAsync(GoogleLoginInfo info)
    {
        await ThrowIfInvalid(_googleLoginValidator, info);

        // O AppService devolve null para token inválido/expirado: vira erro de campo, não 401,
        // porque a credencial apresentada não é deste sistema (research §8).
        var providerUser = await _oauthTokenAppService.ValidateIdTokenAsync(info.IdToken);

        if (providerUser is null)
        {
            throw ValidationFieldError(ID_TOKEN_FIELD, "Token do Google inválido ou expirado.");
        }

        if (!providerUser.EmailVerified || string.IsNullOrWhiteSpace(providerUser.Email))
        {
            throw ValidationFieldError(ID_TOKEN_FIELD, "O provedor não retornou um e-mail verificado.");
        }

        var email = User.NormalizeEmail(providerUser.Email);
        var name = ResolveName(providerUser.Name, email);

        var user = await UpsertAsync(email, name, providerUser.Subject);

        return BuildLoginResult(user);
    }

    /// <summary>
    /// Localiza pelo e-mail normalizado e, se não existir, cria como conta Google. Nome/subject
    /// do provedor são reatualizados a cada login (data-model). A corrida entre duas sessões do
    /// mesmo e-mail é resolvida relendo o usuário após o <see cref="DuplicateException"/> do
    /// índice único <c>ux_users_email</c> (research §11).
    /// <para>
    /// Reutilizar a conta pelo Google <b>remove a senha</b> dela (FR-009): o Google comprova a
    /// posse do e-mail, e a conta passa a satisfazer o invariante do data-model
    /// <c>Provider = Google ⇒ PasswordHash = NULL</c>.
    /// </para>
    /// </summary>
    private async Task<User> UpsertAsync(string email, string name, string? subject)
    {
        var existing = await _users.GetByEmailAsync(email);

        if (existing is not null)
        {
            return await _users.UpdateAsync(ClaimedByGoogle(existing, name, subject));
        }

        var now = _clock.UtcNow;
        var created = new User
        {
            Email = email,
            Name = name,
            Provider = AuthProvider.Google,
            ProviderSubject = subject,
            CreatedAt = now,
            UpdatedAt = now
        };

        try
        {
            return await _users.InsertAsync(created);
        }
        catch (DuplicateException)
        {
            var winner = await _users.GetByEmailAsync(email);

            if (winner is null)
            {
                throw;
            }

            // A corrida pode ter sido ganha por um cadastro (conta com senha): o Google manda
            // nela do mesmo jeito, senão o invariante do data-model ficaria violado só nesta
            // linha de tempo improvável.
            return await _users.UpdateAsync(ClaimedByGoogle(winner, name, subject));
        }
    }

    /// <summary>
    /// Aplica ao usuário a entrada pelo Google (FR-009): nome/subject atualizados, provedor
    /// marcado como Google e senha removida se a conta tinha. Devolve o próprio usuário para o
    /// chamador persistir.
    /// </summary>
    private User ClaimedByGoogle(User user, string name, string? subject)
    {
        var now = _clock.UtcNow;

        user.UpdateFromProvider(name, subject, now);
        user.Provider = AuthProvider.Google;

        if (user.HasPassword)
        {
            user.ClearPassword(now);
        }

        return user;
    }

    /// <summary>
    /// Nome exibido: o declarado pelo provedor ou, quando ele vem vazio, a parte local do e-mail
    /// (data-model: <c>users.name</c> é não-nulo e o Google pode omitir o claim <c>name</c>).
    /// </summary>
    private static string ResolveName(string providerName, string email)
    {
        if (!string.IsNullOrWhiteSpace(providerName))
        {
            return providerName.Trim();
        }

        var at = email.IndexOf('@');

        return at > 0 ? email[..at] : email;
    }

    private LoginResult BuildLoginResult(User user)
    {
        var (token, expiresAt) = _jwtTokenAppService.Generate(user.Id, user.Email, user.Name);

        return new LoginResult
        {
            Token = token,
            ExpiresAt = expiresAt,
            User = MapToUserInfo(user)
        };
    }

    /// <summary>Mapeamento manual (skill <c>dotnet-architecture</c>): nunca inclui a senha.</summary>
    private static UserInfo MapToUserInfo(User user) => new()
    {
        Id = user.Id,
        Email = user.Email,
        Name = user.Name,
        Provider = user.Provider,
        CreatedAt = user.CreatedAt
    };

    private static async Task ThrowIfInvalid<T>(IValidator<T> validator, T target)
    {
        var result = await validator.ValidateAsync(target);

        if (!result.IsValid)
        {
            throw new ValidationException(result.Errors);
        }
    }

    private static ValidationException ValidationFieldError(string field, string message) =>
        new([new ValidationFailure(field, message)]);
}
