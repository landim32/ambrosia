using Ambrosio.DTO.Imports;
using FluentValidation;

namespace Ambrosio.Domain.Validators;

/// <summary>
/// Validação do cadastro de credenciais do +Vendas na tela de configuração (feature 008, US1,
/// FR-001/FR-002, data-model §7). É o mesmo formato da recusa da feature 006: <b>nenhuma mensagem
/// ecoa o valor enviado</b> — nada de <c>{PropertyValue}</c> — porque a mensagem vai para o corpo da
/// resposta <c>400</c> e pode ser lida por outra superfície (log do navegador, captura de tela).
/// <para>
/// O <c>null</c> e a string vazia significam coisas diferentes aqui, e é a regra mais fácil de
/// estragar num refactor: <c>null</c> é "não mexi nesse campo" (opção legítima, é assim que se troca
/// só a senha), enquanto <c>""</c> e <c>"   "</c> são valor enviado e recusado ("Não aceita string
/// vazia nem só espaços", FR-002). As regras por campo por isso têm <c>When</c> explícito.
/// </para>
/// <para>
/// As duas checagens que dependem do que já está gravado — "Informe a senha junto com o login." e
/// "Informe a senha para trocar o login." — não podem estar aqui: exigiriam comparar o login com a
/// cifra, que é da Infra. Vivem no <c>MaisVendasCredentialService</c>, com as mesmas mensagens e o
/// mesmo campo (<c>password</c>) que o contrato define.
/// </para>
/// </summary>
public class MaisVendasCredentialsUpdateInfoValidator : AbstractValidator<MaisVendasCredentialsUpdateInfo>
{
    /// <summary>Mesmo teto da coluna <c>login</c> da feature 006.</summary>
    private const int LOGIN_MAX_LENGTH = 120;

    /// <summary>Mesmo teto da coluna <c>password</c> da feature 006.</summary>
    private const int PASSWORD_MAX_LENGTH = 200;

    public MaisVendasCredentialsUpdateInfoValidator()
    {
        // Campo presente (não nulo) é campo bem-formado: vazio ou só espaço não é "omiti". O `When` é
        // da RAIZ (FluentValidation não passa o valor da propriedade para `When`), por isso a checagem
        // é `info.Login is not null` e não uma comparação sobre o próprio valor.
        RuleFor(x => x.Login)
            .Cascade(CascadeMode.Stop)
            .Must(login => !string.IsNullOrWhiteSpace(login))
                .WithMessage("Informe o login do +Vendas.")
            .MaximumLength(LOGIN_MAX_LENGTH)
                .WithMessage("O login deve ter no máximo 120 caracteres.")
            .When(info => info.Login is not null);

        RuleFor(x => x.Password)
            .Cascade(CascadeMode.Stop)
            .Must(password => !string.IsNullOrWhiteSpace(password))
                .WithMessage("Informe a senha do +Vendas.")
            .MaximumLength(PASSWORD_MAX_LENGTH)
                .WithMessage("A senha deve ter no máximo 200 caracteres.")
            .When(info => info.Password is not null);
    }
}
