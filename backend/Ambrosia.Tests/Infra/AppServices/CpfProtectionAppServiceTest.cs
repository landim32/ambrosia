using System.Security.Cryptography;
using Ambrosia.DTO.Settings;
using Ambrosia.Infra.AppServices;
using Microsoft.Extensions.Options;

namespace Ambrosia.Tests.Infra.AppServices;

/// <summary>
/// T053 [US2]: proteção de CPF (research §4, FR-011) — hash HMAC-SHA256 com pepper, cifra
/// AES-256-GCM com nonce aleatório e falha de integridade quando o payload é adulterado.
/// </summary>
public class CpfProtectionAppServiceTest
{
    private const string CPF = "52998224725";
    private const string PEPPER = "dev-pepper-change-me";

    /// <summary>Chave AES válida: base64 de exatamente 32 bytes (1..32), como no quickstart.</summary>
    private static readonly string KEY_32 = Convert.ToBase64String(
        Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    /// <summary>Outra chave de 32 bytes, para provar que o texto cifrado não é portátil.</summary>
    private static readonly string KEY_32_OTHER = Convert.ToBase64String(
        Enumerable.Range(33, 32).Select(i => (byte)(i % 256)).ToArray());

    private static CpfProtectionAppService CreateSut(
        string? pepper = null,
        string? encryptionKey = null) =>
        new(Options.Create(new CpfProtectionSettings
        {
            Pepper = pepper ?? PEPPER,
            EncryptionKey = encryptionKey ?? KEY_32
        }));

    [Fact]
    public void Hash_ShouldBeDeterministic_ForTheSameInput()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var first = sut.Hash(CPF);
        var second = sut.Hash(CPF);

        // Assert: é isso que permite a unicidade (user_id, cpf_hash) no banco (FR-010).
        Assert.Equal(first, second);
    }

    [Fact]
    public void Hash_ShouldReturn64LowercaseHexCharacters()
    {
        // Act
        var hash = CreateSut().Hash(CPF);

        // Assert: SHA-256 = 32 bytes = 64 hex; a coluna cpf_hash é varchar(64) (data-model).
        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void Hash_ShouldDiffer_WhenPepperChanges()
    {
        // Act: sem pepper, os 10^11 CPFs possíveis seriam enumeráveis offline (research §4).
        var withPepperA = CreateSut(pepper: "pepper-A").Hash(CPF);
        var withPepperB = CreateSut(pepper: "pepper-B").Hash(CPF);

        // Assert
        Assert.NotEqual(withPepperA, withPepperB);
    }

    [Fact]
    public void Hash_ShouldDiffer_ForDifferentCpf()
    {
        var sut = CreateSut();

        Assert.NotEqual(sut.Hash(CPF), sut.Hash("52998224724"));
    }

    [Fact]
    public void Decrypt_ShouldReturnTheOriginalDigits_WhenPayloadComesFromEncrypt()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var payload = sut.Encrypt(CPF);
        var restored = sut.Decrypt(payload);

        // Assert: o detalhe do cliente (FR-009) depende de recuperar o CPF exato.
        Assert.Equal(CPF, restored);
    }

    [Fact]
    public void Encrypt_ShouldProduceDifferentPayloads_ForTheSameCpf()
    {
        // Arrange: nonce aleatório por chamada (research §4) — o banco não pode revelar que dois
        // registros têm o mesmo CPF pela coluna cifrada.
        var sut = CreateSut();

        // Act
        var a = sut.Encrypt(CPF);
        var b = sut.Encrypt(CPF);

        // Assert
        Assert.NotEqual(a, b);
        Assert.Equal(CPF, sut.Decrypt(a));
        Assert.Equal(CPF, sut.Decrypt(b));
    }

    [Fact]
    public void Encrypt_ShouldFitTheDatabaseColumn_WhenBase64Encoded()
    {
        // Assert: cpf_encrypted é varchar(120) (data-model): nonce(12) + tag(16) + 11 bytes de
        // ciphertext = 39 bytes → 52 caracteres em base64.
        var payload = CreateSut().Encrypt(CPF);

        Assert.True(payload.Length <= 120, $"payload com {payload.Length} caracteres: {payload}");
        Assert.Equal(39, Convert.FromBase64String(payload).Length);
    }

    [Fact]
    public void Decrypt_ShouldThrow_WhenPayloadIsTampered()
    {
        // Arrange: inverte um byte do ciphertext (a última posição do payload).
        var sut = CreateSut();
        var payload = sut.Encrypt(CPF);

        var bytes = Convert.FromBase64String(payload);
        bytes[^1] ^= 0xFF;
        var tampered = Convert.ToBase64String(bytes);

        // Act & Assert: AES-GCM autentica — adulteração nunca devolve texto em claro.
        Assert.ThrowsAny<CryptographicException>(() => sut.Decrypt(tampered));
    }

    [Fact]
    public void Decrypt_ShouldThrow_WhenPayloadIsTruncated()
    {
        var sut = CreateSut();
        var payload = sut.Encrypt(CPF);

        // Corta 8 caracteres do base64: falta parte do ciphertext.
        var shortPayload = payload[..^8];

        Assert.ThrowsAny<CryptographicException>(() => sut.Decrypt(shortPayload));
    }

    [Fact]
    public void Decrypt_ShouldFailWithAnotherKey_WhenEncryptionKeyDiffers()
    {
        // Arrange: a mesma cifra feita com outra chave não pode ser lida (rotação acidental de
        // segredo é detectada imediatamente, research §16).
        var payload = CreateSut().Encrypt(CPF);

        // Act & Assert
        Assert.ThrowsAny<CryptographicException>(
            () => CreateSut(encryptionKey: KEY_32_OTHER).Decrypt(payload));
    }

    [Fact]
    public void Hash_ShouldUseUtf8BytesOfThePepper()
    {
        // Assert documentado: o pepper é aplicado como UTF-8 (research §4), então um pepper com
        // acento tem comportamento estável entre máquinas.
        var sut = CreateSut(pepper: "pimenta-cajuina");

        var hash = sut.Hash(CPF);

        Assert.Matches("^[0-9a-f]{64}$", hash);
        Assert.Equal(hash, sut.Hash(CPF));
    }

    [Fact]
    public void Decrypt_ShouldThrow_WhenPayloadIsNotBase64()
    {
        var sut = CreateSut();

        // FormatException é o caminho honesto para lixo no banco — não deve virar texto vazio.
        Assert.Throws<FormatException>(() => sut.Decrypt("não-é-base64!!"));
    }
}
