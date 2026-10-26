using System.Reflection;
using Ambrosia.Domain.Exceptions;
using Ambrosia.Domain.Interfaces;
using Ambrosia.Domain.Models;
using Ambrosia.Domain.Services;
using Ambrosia.DTO.Enums;
using Ambrosia.DTO.Users;
using Ambrosia.Infra.Interfaces.Repository;
using Moq;

namespace Ambrosia.Tests.Domain.Services;

/// <summary>
/// T030 [US1]: <c>GET /users/me</c> devolve o perfil sem qualquer campo de senha (FR-003).
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

    private static User NewUser() => new()
    {
        Id = 3,
        Email = "ana@exemplo.com",
        Name = "Ana",
        PasswordHash = "hash-secreto-nao-pode-sair-daqui",
        Provider = AuthProvider.Dev,
        ProviderSubject = null,
        CreatedAt = CREATED,
        UpdatedAt = CREATED
    };

    [Fact]
    public async Task GetByIdAsync_ShouldMapProfileWithoutPassword()
    {
        // Arrange
        _users.Setup(r => r.GetByIdAsync(3)).ReturnsAsync(NewUser());

        // Act
        var result = await _sut.GetByIdAsync(userId: 3);

        // Assert
        Assert.Equal(3, result.Id);
        Assert.Equal("ana@exemplo.com", result.Email);
        Assert.Equal("Ana", result.Name);
        Assert.Equal(AuthProvider.Dev, result.Provider);
        Assert.Equal(CREATED, result.CreatedAt);
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
