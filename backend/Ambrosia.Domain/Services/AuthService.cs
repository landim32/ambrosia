using Ambrosia.Domain.Exceptions;
using Ambrosia.Domain.Interfaces;
using Ambrosia.Domain.Models;
using Ambrosia.DTO.Auth;
using Ambrosia.DTO.Enums;
using Ambrosia.DTO.Users;
using Ambrosia.Infra.Interfaces.AppServices;
using Ambrosia.Infra.Interfaces.Repository;
using FluentValidation;
using FluentValidation.Results;

namespace Ambrosia.Domain.Services;

/// <summary>
/// Regras de login da feature (FR-001, FR-002, FR-021): valida o DTO de entrada, resolve o
/// provedor externo pela abstração da Infra, faz o <i>upsert</i> do usuário pelo e-mail
/// normalizado e emite o JWT do Ambrosia com <c>sub = user_id</c>.
/// </summary>
public class AuthService : IAuthService
{
    /// <summary>Campo citado nos erros de validação do fluxo Google (FR-024).</summary>
    private const string ID_TOKEN_FIELD = "idToken";

    private readonly IUserRepository<User> _users;
    private readonly IOAuthTokenAppService _oauthTokenAppService;
    private readonly IJwtTokenAppService _jwtTokenAppService;
    private readonly IValidator<GoogleLoginInfo> _googleLoginValidator;
    private readonly IValidator<DevLoginInfo> _devLoginValidator;
    private readonly IClock _clock;

    public AuthService(
        IUserRepository<User> users,
        IOAuthTokenAppService oauthTokenAppService,
        IJwtTokenAppService jwtTokenAppService,
        IValidator<GoogleLoginInfo> googleLoginValidator,
        IValidator<DevLoginInfo> devLoginValidator,
        IClock clock)
    {
        _users = users;
        _oauthTokenAppService = oauthTokenAppService;
        _jwtTokenAppService = jwtTokenAppService;
        _googleLoginValidator = googleLoginValidator;
        _devLoginValidator = devLoginValidator;
        _clock = clock;
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

        var user = await UpsertAsync(email, name, providerUser.Subject, AuthProvider.Google);

        return BuildLoginResult(user);
    }

    /// <inheritdoc />
    public async Task<LoginResult> DevLoginAsync(DevLoginInfo info)
    {
        await ThrowIfInvalid(_devLoginValidator, info);

        var email = User.NormalizeEmail(info.Email);
        var user = await UpsertAsync(email, info.Name.Trim(), subject: null, AuthProvider.Dev);

        return BuildLoginResult(user);
    }

    /// <summary>
    /// Localiza pelo e-mail normalizado e, se não existir, cria. Nome/subject do provedor são
    /// reatualizados a cada login (data-model). A corrida entre duas sessões do mesmo e-mail é
    /// resolvida relendo o usuário após o <see cref="DuplicateException"/> do índice único
    /// <c>ux_users_email</c> (research §11).
    /// </summary>
    private async Task<User> UpsertAsync(
        string email,
        string name,
        string? subject,
        AuthProvider provider)
    {
        var existing = await _users.GetByEmailAsync(email);

        if (existing is not null)
        {
            existing.UpdateFromProvider(name, subject, _clock.UtcNow);
            return await _users.UpdateAsync(existing);
        }

        var now = _clock.UtcNow;
        var created = new User
        {
            Email = email,
            Name = name,
            Provider = provider,
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

            return winner;
        }
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
