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

    [Fact]
    public async Task Can_change_plan_and_capacity_addon_independently()
    {
        var response = await _service.CreateAsync(new CreateTenantRequest(
            "Acme Parking", "acme-parking", "Growth", "PHP", "Asia/Manila",
            "Ada", "Admin", "ada@acme.test", "StrongPass!2026"), CancellationToken.None);

        var withCapacity = await _service.UpdateCapacityAddonAsync(
            response.Id, new UpdateTenantCapacityAddonRequest(20, "Approved capacity exception"), CancellationToken.None);
        withCapacity.AdditionalSlotCapacity.Should().Be(20);
        withCapacity.EffectiveMaximumSlotsPerLocation.Should().Be(70);

        var changedPlan = await _service.ChangePlanAsync(
            response.Id, new UpdateTenantPlanRequest("Enterprise", "Approved plan change"), CancellationToken.None);
        changedPlan.SubscriptionPlan.Should().Be("Enterprise");
        changedPlan.AdditionalSlotCapacity.Should().Be(20);
    }

    [Fact]
    public async Task Starter_can_be_onboarded_with_ten_slots_at_fifteen_hundred_pesos_per_month()
    {
        var response = await _service.CreateAsync(new CreateTenantRequest(
            "Small Parking", "small-parking", "Starter", "PHP", "Asia/Manila",
            "Ada", "Admin", "small@parking.test", "StrongPass!2026",
            PurchasedSlotCapacityPerLocation: 10), CancellationToken.None);

        response.PurchasedSlotCapacityPerLocation.Should().Be(10);
        response.EffectiveMaximumSlotsPerLocation.Should().Be(10);
        response.MonthlyPrice.Should().Be(1500m);
        response.PricePerSlot.Should().Be(150m);
        response.CapacityPricingEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Adding_capacity_recalculates_the_monthly_price_from_the_selected_capacity()
    {
        var response = await _service.CreateAsync(new CreateTenantRequest(
            "Small Parking", "small-parking", "Starter", "PHP", "Asia/Manila",
            "Ada", "Admin", "small@parking.test", "StrongPass!2026",
            PurchasedSlotCapacityPerLocation: 10), CancellationToken.None);

        var updated = await _service.UpdateCapacityAddonAsync(
            response.Id,
            new UpdateTenantCapacityAddonRequest(5, "Approved five-slot expansion"),
            CancellationToken.None);

        updated.EffectiveMaximumSlotsPerLocation.Should().Be(15);
        updated.MonthlyPrice.Should().Be(2250m);
    }

    [Fact]
    public async Task Changing_plan_recalculates_price_without_losing_selected_capacity()
    {
        var response = await _service.CreateAsync(new CreateTenantRequest(
            "Small Parking", "small-parking", "Starter", "PHP", "Asia/Manila",
            "Ada", "Admin", "small@parking.test", "StrongPass!2026",
            PurchasedSlotCapacityPerLocation: 10), CancellationToken.None);

        var changed = await _service.ChangePlanAsync(
            response.Id,
            new UpdateTenantPlanRequest("Growth", "Approved plan change"),
            CancellationToken.None);

        changed.PurchasedSlotCapacityPerLocation.Should().Be(10);
        changed.EffectiveMaximumSlotsPerLocation.Should().Be(10);
        changed.MonthlyPrice.Should().Be(1200m);
    }
}
