using FluentAssertions;
using SmartDoc.Domain.Entities;

namespace SmartDoc.UnitTests.Domain;

public class UserTests
{
    [Fact]
    public void Constructor_WithValidData_SetsProperties()
    {
        var id = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;

        var user = new User(id, "someone@example.com", "hashed-password", createdAt);

        user.Id.Should().Be(id);
        user.Email.Should().Be("someone@example.com");
        user.PasswordHash.Should().Be("hashed-password");
        user.CreatedAt.Should().Be(createdAt);
    }

    [Fact]
    public void Constructor_WithEmptyId_Throws()
    {
        var act = () => new User(Guid.Empty, "someone@example.com", "hashed-password", DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>().WithParameterName("id");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithEmptyEmail_Throws(string email)
    {
        var act = () => new User(Guid.NewGuid(), email, "hashed-password", DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>().WithParameterName("email");
    }

    [Fact]
    public void Constructor_WithEmailExceedingMaxLength_Throws()
    {
        var tooLongEmail = new string('a', User.MaxEmailLength + 1);

        var act = () => new User(Guid.NewGuid(), tooLongEmail, "hashed-password", DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>().WithParameterName("email");
    }

    [Fact]
    public void Constructor_WithEmailAtMaxLength_DoesNotThrow()
    {
        var maxLengthEmail = new string('a', User.MaxEmailLength);

        var act = () => new User(Guid.NewGuid(), maxLengthEmail, "hashed-password", DateTimeOffset.UtcNow);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithEmptyPasswordHash_Throws(string passwordHash)
    {
        var act = () => new User(Guid.NewGuid(), "someone@example.com", passwordHash, DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>().WithParameterName("passwordHash");
    }

    [Fact]
    public void SetPasswordHash_WithValidValue_UpdatesPasswordHash()
    {
        var user = new User(Guid.NewGuid(), "someone@example.com", "old-hash", DateTimeOffset.UtcNow);

        user.SetPasswordHash("new-hash");

        user.PasswordHash.Should().Be("new-hash");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SetPasswordHash_WithEmptyValue_Throws(string passwordHash)
    {
        var user = new User(Guid.NewGuid(), "someone@example.com", "old-hash", DateTimeOffset.UtcNow);

        var act = () => user.SetPasswordHash(passwordHash);

        act.Should().Throw<ArgumentException>().WithParameterName("passwordHash");
    }
}
