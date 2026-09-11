using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ParkingSaaS.Application.Platform;
using ParkingSaaS.Contracts.Platform;
using ParkingSaaS.Domain.Emails;
using ParkingSaaS.Domain.Users;
using ParkingSaaS.Infrastructure.Identity;
using ParkingSaaS.Infrastructure.Persistence;
using ParkingSaaS.UnitTests.Common;

namespace ParkingSaaS.UnitTests.Platform;

public sealed class PlatformAdministratorServiceTests
{
    private readonly MutableTenantContext _tenant = new();
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 7, 10, 9, 0, 0, TimeSpan.Zero));
    private readonly AppDbContext _db;
    private readonly PlatformAdministratorService _service;

    public PlatformAdministratorServiceTests()
    {
        _tenant.ScopeToPlatformAdmin();
        _db = InMemoryDb.Create(_tenant);
        _service = new PlatformAdministratorService(_db, new PasswordHasher(), TestEmail.Queue(_db), _clock);
    }

    [Fact]
    public async Task InviteAsync_creates_tenantless_platform_admin_and_queues_invitation()
    {
        var response = await _service.InviteAsync(
            new InvitePlatformAdministratorRequest("Ada", "Admin", "Ada@Example.test"),
            CancellationToken.None);

        response.Administrator.Email.Should().Be("ada@example.test");
        response.Administrator.MustChangePassword.Should().BeTrue();
        response.EmailQueued.Should().BeTrue();
        response.TemporaryPassword.Should().HaveLength(16);
        response.ExistingAccount.Should().BeFalse();

        var administrator = await _db.Users
            .IgnoreQueryFilters()
            .Include(u => u.Roles)
            .SingleAsync();
        administrator.TenantId.Should().Be(Guid.Empty);
        administrator.HasRole(RoleType.PlatformAdministrator).Should().BeTrue();
        response.TemporaryPassword.Should().NotBeNull();
        new PasswordHasher().Verify(administrator.PasswordHash, response.TemporaryPassword!, out _).Should().BeTrue();

        var email = await _db.Emails.SingleAsync();
        email.Kind.Should().Be(EmailKind.PlatformAdminInvitation);
        email.TenantId.Should().BeNull();
        email.ToEmail.Should().Be("ada@example.test");
        email.Status.Should().Be(EmailStatus.Pending);
        email.TextBody.Should().Contain(response.TemporaryPassword);
    }

    [Fact]
    public async Task InviteAsync_reuses_an_existing_tenant_account_without_replacing_its_password()
    {
        var tenantId = Guid.NewGuid();
        var passwordHash = new PasswordHasher().Hash("Existing!Password2026");
        var existing = new ApplicationUser(
            tenantId, "Existing", "User", "existing@example.test", passwordHash);
        existing.AddRole(RoleType.TenantAdministrator, tenantId);
        _db.Users.Add(existing);
        await _db.SaveChangesAsync();

        var response = await _service.InviteAsync(
            new InvitePlatformAdministratorRequest("Another", "Admin", "EXISTING@example.test"),
            CancellationToken.None);

        response.ExistingAccount.Should().BeTrue();
        response.TemporaryPassword.Should().BeNull();
        response.Administrator.Email.Should().Be("existing@example.test");
        response.Administrator.FirstName.Should().Be("Existing");

        var administrator = await _db.Users
            .IgnoreQueryFilters()
            .Include(u => u.Roles)
            .Include(u => u.Memberships)
            .SingleAsync();
        administrator.Memberships.Should().HaveCount(2);
        administrator.Roles.Should().Contain(r => r.TenantId == tenantId && r.Role == RoleType.TenantAdministrator);
        administrator.Roles.Should().Contain(r => r.TenantId == Guid.Empty && r.Role == RoleType.PlatformAdministrator);
        new PasswordHasher().Verify(administrator.PasswordHash, "Existing!Password2026", out _).Should().BeTrue();

        var email = await _db.Emails.SingleAsync();
        email.Kind.Should().Be(EmailKind.PlatformAdminAccessGranted);
        email.TextBody.Should().NotContain("Temporary password");
    }

    [Fact]
    public async Task InviteAsync_rejects_a_duplicate_active_platform_membership()
    {
        var existing = new ApplicationUser(
            Guid.NewGuid(), "Existing", "User", "existing@example.test", "hash");
        existing.AddRole(RoleType.PlatformAdministrator, Guid.Empty);
        _db.Users.Add(existing);
        await _db.SaveChangesAsync();

        _db.Users.Add(new ApplicationUser(
            Guid.NewGuid(), "Other", "User", "other@example.test", "hash"));
        var act = () => _service.InviteAsync(
            new InvitePlatformAdministratorRequest("Another", "Admin", "EXISTING@example.test"),
            CancellationToken.None);

        await act.Should().ThrowAsync<ParkingSaaS.Application.Common.Exceptions.ConflictException>();
    }

    [Fact]
    public async Task ListAsync_returns_only_platform_administrators()
    {
        var platform = new ApplicationUser(Guid.Empty, "Platform", "Admin", "platform@example.test", "hash");
        platform.AddRole(RoleType.PlatformAdministrator);
        var tenantUser = new ApplicationUser(Guid.NewGuid(), "Tenant", "Admin", "tenant@example.test", "hash");
        tenantUser.AddRole(RoleType.TenantAdministrator);
        _db.Users.AddRange(platform, tenantUser);
        await _db.SaveChangesAsync();

        var result = await _service.ListAsync(CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Email.Should().Be("platform@example.test");
    }
}
