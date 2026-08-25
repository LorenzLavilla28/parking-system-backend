using FluentAssertions;
using ParkingSaaS.Application.Benefits;
using ParkingSaaS.Domain.Benefits;
using ParkingSaaS.Domain.Locations;
using ParkingSaaS.Domain.Sessions;
using ParkingSaaS.Infrastructure.Persistence;
using ParkingSaaS.UnitTests.Common;
using Xunit;

namespace ParkingSaaS.UnitTests.Benefits;

public sealed class CorporateBenefitAllocationTests
{
    [Fact]
    public async Task Only_one_vehicle_can_claim_a_single_shared_slot()
    {
        var (db, tenantId, location, program) = CreateBenefit(capacity: 1);
        var allocator = new CorporateBenefitAllocationService(db);
        var at = At();

        var first = await allocator.TryAllocateAsync(tenantId, location.Id, Guid.NewGuid(), VehicleType.Car, at, CancellationToken.None, program.Id);
        db.SaveChanges();
        var second = await allocator.TryAllocateAsync(tenantId, location.Id, Guid.NewGuid(), VehicleType.Car, at, CancellationToken.None, program.Id);

        first.Applied.Should().BeTrue();
        second.Applied.Should().BeFalse();
        second.Message.Should().Contain("standard rate");
        db.CorporateBenefitAllocations.Count().Should().Be(1);
    }

    [Fact]
    public async Task Releasing_the_session_makes_the_shared_slot_available_again()
    {
        var (db, tenantId, location, program) = CreateBenefit(capacity: 1);
        var allocator = new CorporateBenefitAllocationService(db);
        var at = At();
        var sessionId = Guid.NewGuid();

        var first = await allocator.TryAllocateAsync(tenantId, location.Id, sessionId, VehicleType.Car, at, CancellationToken.None, program.Id);
        db.SaveChanges();
        await allocator.ReleaseForSessionAsync(sessionId, at.AddHours(1), CancellationToken.None);
        db.SaveChanges();
        var second = await allocator.TryAllocateAsync(tenantId, location.Id, Guid.NewGuid(), VehicleType.Car, at.AddHours(2), CancellationToken.None, program.Id);

        first.Applied.Should().BeTrue();
        second.Applied.Should().BeTrue();
    }

    [Fact]
    public async Task Allocation_requires_a_guard_selected_program()
    {
        var (db, tenantId, location, _) = CreateBenefit(capacity: 1);
        var allocator = new CorporateBenefitAllocationService(db);

        var result = await allocator.TryAllocateAsync(tenantId, location.Id, Guid.NewGuid(), VehicleType.Car, At(), CancellationToken.None);

        result.Applied.Should().BeFalse();
        db.CorporateBenefitAllocations.Count().Should().Be(0);
    }

    private static (AppDbContext Db, Guid TenantId, ParkingLocation Location, CorporateBenefitProgram Program) CreateBenefit(int capacity)
    {
        var tenantId = Guid.NewGuid();
        var tenant = new MutableTenantContext();
        tenant.ScopeTo(tenantId);
        var db = InMemoryDb.Create(tenant);
        var location = new ParkingLocation(tenantId, "Lot", "lot", "Asia/Manila", null);
        db.ParkingLocations.Add(location);

        var program = new CorporateBenefitProgram(tenantId, "ABC Corporation", "", 0);
        program.Activate();
        db.CorporateBenefitPrograms.Add(program);
        db.CorporateBenefitProgramLocations.Add(new CorporateBenefitProgramLocation(tenantId, program.Id, location.Id, capacity));
        db.CorporateBenefitProgramVersions.Add(new CorporateBenefitProgramVersion(
            tenantId, program.Id, 1, new DateTimeOffset(2026, 6, 23, 0, 0, 0, TimeSpan.Zero),
            new CorporateBenefitRules().Serialize(), Guid.NewGuid()));
        db.SaveChanges();
        return (db, tenantId, location, program);
    }

    private static DateTimeOffset At() => new(2026, 6, 24, 8, 30, 0, TimeSpan.FromHours(8));
}
