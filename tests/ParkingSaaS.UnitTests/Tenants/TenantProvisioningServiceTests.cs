using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ParkingSaaS.Application.Tenants;
using ParkingSaaS.Contracts.Tenants;
using ParkingSaaS.Domain.Emails;
using ParkingSaaS.Domain.Users;
using ParkingSaaS.Infrastructure.Identity;
using ParkingSaaS.UnitTests.Common;
using Xunit;

namespace ParkingSaaS.UnitTests.Tenants;

public sealed class TenantProvisioningServiceTests
{
    private readonly MutableTenantContext _tenant = new();
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 7, 10, 9, 0, 0, TimeSpan.Zero));
    private readonly ParkingSaaS.Infrastructure.Persistence.AppDbContext _db;
    private readonly TenantProvisioningService _service;

    public TenantProvisioningServiceTests()
    {
        _tenant.ScopeToPlatformAdmin();
        _db = InMemoryDb.Create(_tenant);
        _service = new TenantProvisioningService(_db, new PasswordHasher(), TestEmail.Queue(_db), _clock);
    }

    [Fact]
    public async Task CreateAsync_provisions_tenant_and_admin_without_a_location()
    {
        var request = new CreateTenantRequest(
            Name: "Acme Parking",
            Slug: "acme-parking",
            SubscriptionPlan: "Growth",
            DefaultCurrency: "PHP",
            DefaultTimezone: "Asia/Manila",
            AdminFirstName: "Ada",
            AdminLastName: "Admin",
            AdminEmail: "Ada@Acme.test",
            AdminPassword: "StrongPass!2026");

        var response = await _service.CreateAsync(request, CancellationToken.None);

        response.Name.Should().Be("Acme Parking");
        var tenant = await _db.Tenants.IgnoreQueryFilters().SingleAsync();
        var admin = await _db.Users.IgnoreQueryFilters().SingleAsync();
        var locationCount = await _db.ParkingLocations.IgnoreQueryFilters().CountAsync();

        admin.TenantId.Should().Be(tenant.Id);
        admin.Email.Should().Be("ada@acme.test");
        admin.HasRole(RoleType.TenantAdministrator).Should().BeTrue();
        admin.MustChangePassword.Should().BeTrue();

        locationCount.Should().Be(0);

        // An onboarding email is queued to the new administrator in the same transaction.
        var email = await _db.Emails.SingleAsync();
        email.Kind.Should().Be(EmailKind.TenantOnboarding);
        email.ToEmail.Should().Be("ada@acme.test");
        email.Status.Should().Be(EmailStatus.Pending);
        email.TextBody.Should().Contain("Temporary password: StrongPass!2026");
    }
}
