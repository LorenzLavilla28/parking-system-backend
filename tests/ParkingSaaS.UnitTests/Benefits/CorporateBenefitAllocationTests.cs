using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ParkingSaaS.Application.Benefits;
using ParkingSaaS.Application.Common.Options;
using ParkingSaaS.Application.Guard;
using ParkingSaaS.Contracts.Guard;
using ParkingSaaS.Domain.Benefits;
using ParkingSaaS.Domain.Locations;
using ParkingSaaS.Domain.Services;
using ParkingSaaS.Domain.Sessions;
using ParkingSaaS.Domain.Users;
using ParkingSaaS.Infrastructure.Persistence;
using ParkingSaaS.Infrastructure.Sessions;
using ParkingSaaS.UnitTests.Common;
using Xunit;

namespace ParkingSaaS.UnitTests.Benefits;

public sealed class CorporateBenefitAllocationTests
{
    [Fact]
    public async Task Only_one_eligible_plate_can_claim_a_single_shared_slot()
    {
        var tenantId = Guid.NewGuid();
        var tenant = new MutableTenantContext();
        tenant.ScopeTo(tenantId);
        var db = InMemoryDb.Create(tenant);
        var location = new ParkingLocation(tenantId, "Lot", "lot", "Asia/Manila", null);
        location.AssignRatePlan(Guid.NewGuid());
        db.ParkingLocations.Add(location);

        var program = new CorporateBenefitProgram(tenantId, "ABC Corporation", "", 0);
        program.Activate();
        db.CorporateBenefitPrograms.Add(program);
        db.CorporateBenefitProgramLocations.Add(new CorporateBenefitProgramLocation(tenantId, program.Id, location.Id, 1));
        db.CorporateBenefitProgramVersions.Add(new CorporateBenefitProgramVersion(
            tenantId, program.Id, 1, new DateTimeOffset(2026, 6, 23, 0, 0, 0, TimeSpan.Zero),
            new CorporateBenefitRules().Serialize(), Guid.NewGuid()));
        db.CorporateBenefitPlates.Add(new CorporateBenefitPlate(tenantId, program.Id, "ABC123", "ABC-123", new DateTimeOffset(2026, 6, 23, 0, 0, 0, TimeSpan.Zero)));
        db.CorporateBenefitPlates.Add(new CorporateBenefitPlate(tenantId, program.Id, "ABC456", "ABC-456", new DateTimeOffset(2026, 6, 23, 0, 0, 0, TimeSpan.Zero)));
        db.SaveChanges();

        var allocator = new CorporateBenefitAllocationService(db);
        var at = new DateTimeOffset(2026, 6, 24, 8, 30, 0, TimeSpan.FromHours(8));
        var first = await allocator.TryAllocateAsync(tenantId, location.Id, Guid.NewGuid(), "ABC123", VehicleType.Car, at, CancellationToken.None);
        db.SaveChanges();
        var second = await allocator.TryAllocateAsync(tenantId, location.Id, Guid.NewGuid(), "ABC456", VehicleType.Car, at, CancellationToken.None);

        first.Applied.Should().BeTrue();
        second.Applied.Should().BeFalse();
        second.Message.Should().Contain("capacity");
        db.CorporateBenefitAllocations.Count().Should().Be(1);
    }

    [Fact]
    public async Task Releasing_the_session_makes_the_shared_slot_available_again()
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
        db.CorporateBenefitProgramLocations.Add(new CorporateBenefitProgramLocation(tenantId, program.Id, location.Id, 1));
        db.CorporateBenefitProgramVersions.Add(new CorporateBenefitProgramVersion(tenantId, program.Id, 1,
            new DateTimeOffset(2026, 6, 23, 0, 0, 0, TimeSpan.Zero), new CorporateBenefitRules().Serialize(), Guid.NewGuid()));
        db.CorporateBenefitPlates.Add(new CorporateBenefitPlate(tenantId, program.Id, "ABC123", "ABC-123", new DateTimeOffset(2026, 6, 23, 0, 0, 0, TimeSpan.Zero)));
        db.CorporateBenefitPlates.Add(new CorporateBenefitPlate(tenantId, program.Id, "ABC456", "ABC-456", new DateTimeOffset(2026, 6, 23, 0, 0, 0, TimeSpan.Zero)));
        db.SaveChanges();

        var allocator = new CorporateBenefitAllocationService(db);
        var at = new DateTimeOffset(2026, 6, 24, 8, 30, 0, TimeSpan.FromHours(8));
        var sessionId = Guid.NewGuid();
        var first = await allocator.TryAllocateAsync(tenantId, location.Id, sessionId, "ABC123", VehicleType.Car, at, CancellationToken.None);
        db.SaveChanges();
        await allocator.ReleaseForSessionAsync(sessionId, at.AddHours(1), CancellationToken.None);
        db.SaveChanges();
        var second = await allocator.TryAllocateAsync(tenantId, location.Id, Guid.NewGuid(), "ABC456", VehicleType.Car, at.AddHours(2), CancellationToken.None);

        first.Applied.Should().BeTrue();
        second.Applied.Should().BeTrue();
    }
}
