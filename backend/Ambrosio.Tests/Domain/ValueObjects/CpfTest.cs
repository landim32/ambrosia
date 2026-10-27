using Ambrosio.Domain.ValueObjects;

namespace Ambrosio.Tests.Domain.ValueObjects;

/// <summary>
/// T051 [US2]: normalização e validação de CPF (research §5) — dígitos, máscara e formatação.
/// </summary>
public class CpfTest
{
    [Theory]
    [InlineData("529.982.247-25")]
    [InlineData("52998224725")]
    [InlineData("  529.982.247-25  ")]
    [InlineData("529.982.24725")]
    [InlineData("529-982.247/25")]
    public void TryParse_ShouldAccept_WhenCheckDigitsAreValid(string raw)
    {
        // Act
        var ok = Cpf.TryParse(raw, out var cpf);

        // Assert: qualquer pontuação/espaço é descartada antes da validação.
        Assert.True(ok);
        Assert.Equal("52998224725", cpf.Digits);
    }

    [Theory]
    [InlineData("111.111.111-11")]
    [InlineData("00000000000")]
    [InlineData("222.222.222-22")]
    [InlineData("99999999999")]
    public void TryParse_ShouldRejectRepeatedSequences_WhenAllDigitsAreTheSame(string raw)
    {
        // A spec (Edge Cases) e o algoritmo clássico exigem rejeitar as sequências repetidas,
        // ainda que em alguns casos os dígitos verificadores "fechem" matematicamente.
        Assert.False(Cpf.TryParse(raw, out _));
    }

    [Theory]
    [InlineData("529.982.247-26")]
    [InlineData("52998224724")]
    [InlineData("123.456.789-01")]
    public void TryParse_ShouldRejectWrongCheckDigits_WhenVerifierIsInvalid(string raw)
    {
        Assert.False(Cpf.TryParse(raw, out _));
    }

    [Theory]
    [InlineData("5299822472")]        // 10 dígitos
    [InlineData("529982247255")]      // 12 dígitos
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData(null)]
    public void TryParse_ShouldReject_WhenDigitCountIsNotEleven(string? raw)
    {
        Assert.False(Cpf.TryParse(raw, out _));
    }

    [Fact]
    public void TryParse_ShouldLeaveNullValue_WhenInvalid()
    {
        // Act
        var ok = Cpf.TryParse("111.111.111-11", out var cpf);

        // Assert: out sempre preenchido (null em inválido), para o chamador não precisar checar
        // duas coisas.
        Assert.False(ok);
        Assert.Null(cpf);
    }

    [Fact]
    public void IsValid_ShouldMirrorTryParse_ForValidatorUse()
    {
        // FR-007: o validator usa IsValid diretamente.
        Assert.True(Cpf.IsValid("529.982.247-25"));
        Assert.False(Cpf.IsValid("111.111.111-11"));
        Assert.False(Cpf.IsValid(null));
    }

    [Fact]
    public void Masked_ShouldKeepOnlyTheMiddleSixDigits()
    {
        // SC-005: listagens nunca expõem o CPF completo.
        Assert.True(Cpf.TryParse("52998224725", out var cpf));

        Assert.Equal("***.982.247-**", cpf.Masked);
        Assert.DoesNotContain("529", cpf.Masked);   // 3 primeiros dígitos escondidos
        Assert.DoesNotContain("25", cpf.Masked);    // 2 últimos escondidos
    }

    [Fact]
    public void Formatted_ShouldReturnTheCanonicalPresentation()
    {
        Assert.True(Cpf.TryParse("52998224725", out var cpf));

        Assert.Equal("529.982.247-25", cpf.Formatted);
    }

    [Fact]
    public void Equality_ShouldCompareNormalizedDigits()
    {
        // FR-010: a unicidade é por CPF normalizado, então 529.982.247-25 e 52998224725 são o
        // mesmo documento.
        Assert.True(Cpf.TryParse("529.982.247-25", out var a));
        Assert.True(Cpf.TryParse("52998224725", out var b));

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }
}
