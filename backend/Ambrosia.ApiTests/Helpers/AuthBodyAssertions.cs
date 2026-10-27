using System.Text.Json;
using FluentAssertions;

namespace Ambrosia.ApiTests.Helpers;

/// <summary>
/// Asserções sobre o corpo bruto das respostas (SC-007, FR-005).
///
/// A checagem é por <b>chave</b>, não por substring: desde a 003 o valor de
/// <c>user.provider</c> é exatamente <c>"password"</c>, então um
/// <c>body.Should().NotContain("password")</c> falharia em toda resposta autenticada sem que
/// nenhum segredo tivesse vazado. Percorrer o JSON procurando o nome da propriedade é o que
/// prova a afirmação real: nem <c>password</c> nem <c>passwordHash</c> saem da API.
/// </summary>
public static class AuthBodyAssertions
{
    /// <summary>Trecho de nome de chave que nunca pode aparecer (diferencia maiúsculas ignorada).</summary>
    private const string PASSWORD_KEY = "password";

    /// <summary>
    /// Falha se alguma chave do JSON contém "password" (pega <c>password</c>,
    /// <c>passwordHash</c>, <c>newPassword</c> …). O corpo bruto é necessário porque a
    /// desserialização para um DTO esconderia a chave extra.
    /// </summary>
    public static void ShouldHaveNoPasswordKey(string body)
    {
        using var document = JsonDocument.Parse(body);

        PasswordKeys(document.RootElement).Should()
            .BeEmpty("nenhuma chave de senha pode sair da API (FR-005, SC-007)");
    }

    /// <summary>Lista, em ordem de visita, as chaves de objeto que citam senha.</summary>
    private static List<string> PasswordKeys(JsonElement element)
    {
        var found = new List<string>();

        Visit(element);

        return found;

        void Visit(JsonElement current)
        {
            if (current.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in current.EnumerateObject())
                {
                    if (property.Name.Contains(PASSWORD_KEY, StringComparison.OrdinalIgnoreCase))
                    {
                        found.Add(property.Name);
                    }

                    Visit(property.Value);
                }
            }
            else if (current.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in current.EnumerateArray())
                {
                    Visit(item);
                }
            }
        }
    }
}
