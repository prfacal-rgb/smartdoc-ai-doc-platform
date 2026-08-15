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

        var user = new User(id, "someone@example.com", createdAt);

        user.Id.Should().Be(id);
        user.Email.Should().Be("someone@example.com");
        user.CreatedAt.Should().Be(createdAt);
    }

    [Fact]
    public void Constructor_WithEmptyId_Throws()
    {
        var act = () => new User(Guid.Empty, "someone@example.com", DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>().WithParameterName("id");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithEmptyEmail_Throws(string email)
    {
        var act = () => new User(Guid.NewGuid(), email, DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>().WithParameterName("email");
    }

    [Fact]
    public void Constructor_WithEmailExceedingMaxLength_Throws()
    {
        var tooLongEmail = new string('a', User.MaxEmailLength + 1);

        var act = () => new User(Guid.NewGuid(), tooLongEmail, DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>().WithParameterName("email");
    }

    [Fact]
    public void Constructor_WithEmailAtMaxLength_DoesNotThrow()
    {
        var maxLengthEmail = new string('a', User.MaxEmailLength);

        var act = () => new User(Guid.NewGuid(), maxLengthEmail, DateTimeOffset.UtcNow);

        act.Should().NotThrow();
    }
}
