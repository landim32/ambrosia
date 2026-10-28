using System.Security.Claims;
using Ambrosio.Domain.Exceptions;
using Ambrosio.Domain.Interfaces;
using Ambrosio.DTO.Auth;
using Ambrosio.DTO.Settings;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Ambrosio.McpServer.Auth.Pages;

/// <summary>
/// Login do authorization server (feature 007, T041, R10): e-mail e senha pelo mesmo
/// <see cref="IAuthService"/> do <c>POST /api/auth/login</c>, ou o ID token do Google pelo mesmo
/// caminho do <c>POST /api/auth/google</c>. O sucesso cria só a sessão do AS (cookie de
/// <c>/auth</c>); o JWT HS256 que o serviço devolve é descartado.
/// </summary>
public sealed class LoginModel(IAuthService auth, IOptions<AuthServerSettings> settings) : PageModel
{
    /// <summary>Única origem aceita para voltar depois do login.</summary>
    public const string AllowedReturnPrefix = "/auth/connect/authorize";

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    /// <summary>ID token entregue pelo botão do Google (GIS).</summary>
    [BindProperty]
    public string? IdToken { get; set; }

    public string? ErrorMessage { get; private set; }

    /// <summary>Login concluído sem <c>returnUrl</c> válido: a tela só avisa, não redireciona.</summary>
    public bool SignedInWithoutReturn { get; private set; }

    public string GoogleClientId => settings.Value.GoogleClientId;

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        LoginResult result;
        try
        {
            result = string.IsNullOrWhiteSpace(IdToken)
                ? await auth.LoginWithPasswordAsync(new LoginInfo { Email = Email, Password = Password })
                : await auth.LoginWithGoogleAsync(new GoogleLoginInfo { IdToken = IdToken });
        }
        catch (InvalidCredentialsException ex)
        {
            // Mesma mensagem para e-mail inexistente e senha errada (FR-008 da feature 003).
            ErrorMessage = ex.Message;
            return Page();
        }
        catch (ValidationException ex)
        {
            ErrorMessage = string.Join(" ", ex.Errors.Select(e => e.ErrorMessage).Distinct());
            return Page();
        }

        var identity = new ClaimsIdentity(McpSetup.AuthSessionScheme, Claims.Name, Claims.Role);
        identity.AddClaim(new Claim(Claims.Subject, result.User.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        identity.AddClaim(new Claim(Claims.Name, result.User.Name));

        await HttpContext.SignInAsync(McpSetup.AuthSessionScheme, new ClaimsPrincipal(identity));

        if (IsSafeReturnUrl(ReturnUrl))
        {
            return LocalRedirect(ReturnUrl!);
        }

        SignedInWithoutReturn = true;

        return Page();
    }

    /// <summary>
    /// Só caminho relativo que volta ao authorize deste servidor: barra dupla, barra invertida e URL
    /// absoluta são recusadas (redirect aberto).
    /// </summary>
    public static bool IsSafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl)
        && returnUrl.StartsWith(AllowedReturnPrefix, StringComparison.Ordinal)
        && (returnUrl.Length == AllowedReturnPrefix.Length || returnUrl[AllowedReturnPrefix.Length] == '?')
        && !returnUrl.Contains('\\')
        && !returnUrl.Contains("//", StringComparison.Ordinal);
}
