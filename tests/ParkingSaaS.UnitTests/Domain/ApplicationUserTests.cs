using FluentAssertions;
using ParkingSaaS.Domain.Users;
using Xunit;

namespace ParkingSaaS.UnitTests.Domain;

public sealed class ApplicationUserTests
{
    private static ApplicationUser NewUser()
        => new(Guid.NewGuid(), "Jane", "Guard", "jane@demo.local", "hash");

    [Fact]
    public void Email_is_normalized_to_lowercase()
    {
        var user = new ApplicationUser(Guid.NewGuid(), "A", "B", "  MixedCase@Demo.LOCAL ", "hash");
        user.Email.Should().Be("mixedcase@demo.local");
    }

    [Fact]
    public void Adding_same_role_twice_is_idempotent()
    {
        var user = NewUser();
        user.AddRole(RoleType.Guard);
        user.AddRole(RoleType.Guard);
        user.Roles.Should().ContainSingle(r => r.Role == RoleType.Guard);
    }

    [Fact]
    public void Assigning_same_location_twice_is_idempotent()
    {
        var user = NewUser();
        var locationId = Guid.NewGuid();
        user.AssignLocation(locationId);
        user.AssignLocation(locationId);
        user.LocationAssignments.Should().ContainSingle(a => a.ParkingLocationId == locationId);
    }

    [Fact]
    public void Account_locks_after_reaching_max_failed_attempts()
    {
        var user = NewUser();
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < 5; i++)
            user.RegisterFailedLogin(now, maxAttempts: 5, lockoutDuration: TimeSpan.FromMinutes(15));

        user.IsLockedOut(now).Should().BeTrue();
        user.FailedLoginAttempts.Should().Be(5);
        // A temporary lockout does not flip the administrative status flag.
        user.CanAuthenticate.Should().BeTrue();
    }

    [Fact]
    public void Lockout_expires_after_the_duration()
    {
        var user = NewUser();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 5; i++)
            user.RegisterFailedLogin(now, 5, TimeSpan.FromMinutes(15));

        user.IsLockedOut(now.AddMinutes(16)).Should().BeFalse();
    }

    [Fact]
    public void Successful_login_resets_failed_attempts()
    {
        var user = NewUser();
        var now = DateTimeOffset.UtcNow;
        user.RegisterFailedLogin(now, 5, TimeSpan.FromMinutes(15));
        user.RegisterFailedLogin(now, 5, TimeSpan.FromMinutes(15));

        user.RegisterSuccessfulLogin();

        user.FailedLoginAttempts.Should().Be(0);
        user.IsLockedOut(now).Should().BeFalse();
    }

    [Fact]
    public void Disabled_user_cannot_authenticate()
    {
        var user = NewUser();
        user.Disable();
        user.CanAuthenticate.Should().BeFalse();
    }
}
