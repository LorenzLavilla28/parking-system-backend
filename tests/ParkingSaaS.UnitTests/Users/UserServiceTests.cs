using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ParkingSaaS.Application.Common;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Application.Users;
using ParkingSaaS.Contracts.Users;
using ParkingSaaS.Domain.Emails;
using ParkingSaaS.Domain.Locations;
using ParkingSaaS.Domain.Tenants;
using ParkingSaaS.Domain.Users;
using ParkingSaaS.Infrastructure.Identity;
using ParkingSaaS.Infrastructure.Persistence;
using ParkingSaaS.UnitTests.Common;
using Xunit;

namespace ParkingSaaS.UnitTests.Users;

public sealed class UserServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly MutableTenantContext _tenant = new();
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 7, 10, 9, 0, 0, TimeSpan.Zero));
    private readonly AppDbContext _db;
    private readonly UserService _service;

    public UserServiceTests()
    {
        _tenant.ScopeTo(_tenantId);
        _db = InMemoryDb.Create(_tenant);
        _db.Tenants.Add(new Tenant("Acme Parking", "acme", SubscriptionPlan.Growth, "PHP", "Asia/Manila"));
        _db.SaveChanges();
        _service = new UserService(_db, _tenant, new PasswordHasher(), TestEmail.Queue(_db), _clock);
    }

    [Fact]
    public async Task Creating_a_user_queues_a_welcome_email()
    {
        var request = new CreateUserRequest(
            "Gina", "Guard", "Gina@Acme.test", "StrongPass!2026",
            new[] { RoleNames.Guard }, null);

        await _service.CreateAsync(request, CancellationToken.None);

        var email = await _db.Emails.SingleAsync();
        email.Kind.Should().Be(EmailKind.UserWelcome);
        email.ToEmail.Should().Be("gina@acme.test");
        email.Status.Should().Be(EmailStatus.Pending);
        email.TextBody.Should().Contain("Temporary password: StrongPass!2026");
        (await _db.Users.SingleAsync()).MustChangePassword.Should().BeTrue();
    }

    [Fact]
    public async Task Updating_a_user_persists_a_location_assignment()
    {
        var location = new ParkingLocation(_tenantId, "Main Gate", "main-gate", "Asia/Manila", null);
        var user = new ApplicationUser(
            _tenantId,
            "Gina",
            "Guard",
            "gina@acme.test",
            "hashed-password");
        user.AddRole(RoleType.Guard);

        _db.ParkingLocations.Add(location);
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var result = await _service.UpdateAsync(
            user.Id,
            new UpdateUserRequest(
                user.FirstName,
                user.LastName,
                new[] { RoleNames.Guard },
                new[] { location.Id },
                true),
            CancellationToken.None);

        result.AssignedLocationIds.Should().ContainSingle().Which.Should().Be(location.Id);
        (await _db.UserParkingLocations.SingleAsync()).ParkingLocationId.Should().Be(location.Id);
    }

    [Fact]
    public async Task Disabling_a_membership_revokes_refresh_tokens_for_that_tenant()
    {
        var user = new ApplicationUser(
            _tenantId,
            "Gina",
            "Guard",
            "gina-refresh@acme.test",
            "hashed-password");
        user.AddRole(RoleType.Guard, _tenantId);
        _db.Users.Add(user);

        var refresh = new RefreshToken(
            user.Id,
            _tenantId,
            new RefreshTokenService().Hash("refresh-token"),
            _clock.UtcNow,
            _clock.UtcNow.AddDays(1),
            null);
        _db.RefreshTokens.Add(refresh);
        await _db.SaveChangesAsync();

        await _service.UpdateAsync(
            user.Id,
            new UpdateUserRequest(
                user.FirstName,
                user.LastName,
                new[] { RoleNames.Guard },
                Array.Empty<Guid>(),
                false),
            CancellationToken.None);

        (await _db.RefreshTokens.SingleAsync()).RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Cannot_disable_the_last_active_tenant_administrator()
    {
        var user = new ApplicationUser(
            _tenantId,
            "Only",
            "Administrator",
            "only-admin@acme.test",
            "hashed-password");
        user.AddRole(RoleType.TenantAdministrator, _tenantId);
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateAsync(
                user.Id,
                new UpdateUserRequest(
                    user.FirstName,
                    user.LastName,
                    new[] { RoleNames.TenantAdministrator },
                    Array.Empty<Guid>(),
                    false),
                CancellationToken.None));

        user.GetMembership(_tenantId)!.IsActive.Should().BeTrue();
    }
}
