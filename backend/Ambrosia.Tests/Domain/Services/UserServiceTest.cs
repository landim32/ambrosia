using System.Reflection;
using Ambrosia.Domain.Exceptions;
using Ambrosia.Domain.Interfaces;
using Ambrosia.Domain.Models;
using Ambrosia.Domain.Services;
using Ambrosia.DTO.Users;
using Ambrosia.Infra.Interfaces.Repository;
using Moq;

namespace Ambrosia.Tests.Domain.Services;

/// <summary>
/// T030 [US1]: <c>GET /users/me</c> devolve o perfil sem qualquer campo de senha (FR-003).
/// Os casos que dependiam de <c>AuthProvider</c> foram apagados pela 003 (constituição §VIII:
/// teste unitário tocado pela autenticação é removido, nunca ajustado); o comportamento do
/// provedor passou a ser coberto por <c>Ambrosia.ApiTests</c>.
/// </summary>
public class UserServiceTest
{
    private static readonly DateTime CREATED = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Unspecified);

    private readonly Mock<IUserRepository<User>> _users = new();
    private readonly UserService _sut;

    public UserServiceTest()
    {
        _sut = new UserService(_users.Object);
    }

    [Fact]
    public void UserInfo_ShouldNotExposeAnyPasswordProperty()
    {
        // Assert (FR-003): nem o nome da propriedade pode aparecer no contrato.
        var offenders = typeof(UserInfo)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Where(n => n.Contains("Password", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldThrowNotFound_WhenUserDoesNotExist()
    {
        // Arrange
        _users.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((User?)null);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetByIdAsync(99));

        Assert.Equal("Usuário não encontrado.", ex.Message);
    }
}
