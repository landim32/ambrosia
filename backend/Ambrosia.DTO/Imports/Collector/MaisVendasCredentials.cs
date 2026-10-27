namespace Ambrosia.DTO.Imports.Collector;

/// <summary>
/// Credenciais do +Vendas para <b>uma</b> sessão de coleta (feature 006, research §4/§14).
/// <para>
/// Objeto de transporte interno, sem <c>[JsonPropertyName]</c>: nada daqui é serializado para resposta
/// nem persistido. Vive apenas enquanto a execução dura e é descartado no <c>finally</c> do job.
/// </para>
/// <para>
/// <see cref="ToString"/> é sobrescrito para que um <c>log</c>, uma interpolated string ou um dump de
/// depuração acidental nunca reproduzam o valor (FR-005/FR-006). Os campos são privados exatamente
/// por isso: quem precisa da senha a lê por <see cref="Password"/> e ninguém guarda a referência.
/// </para>
/// </summary>
public sealed class MaisVendasCredentials
{
    public MaisVendasCredentials(string login, string password)
    {
        Login = login ?? string.Empty;
        Password = password ?? string.Empty;
    }

    /// <summary>Login informado pela vendedora (em homolog, <c>conta+cenario</c>).</summary>
    public string Login { get; }

    /// <summary>Senha informada pela vendedora.</summary>
    public string Password { get; }

    /// <summary>Nunca expõe o conteúdo (research §14).</summary>
    public override string ToString() => "MaisVendasCredentials(oculto)";
}
