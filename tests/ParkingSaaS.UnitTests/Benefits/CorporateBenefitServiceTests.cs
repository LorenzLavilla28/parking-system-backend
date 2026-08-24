using FluentAssertions;
using ParkingSaaS.Application.Audit;
using ParkingSaaS.Application.Benefits;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Contracts.Benefits;
using ParkingSaaS.Domain.Locations;
using ParkingSaaS.Domain.Services;
using ParkingSaaS.Infrastructure.Persistence;
using ParkingSaaS.UnitTests.Common;

namespace ParkingSaaS.UnitTests.Benefits;

public sealed class CorporateBenefitServiceTests
{
    [Fact]
    public async Task Adding_a_revision_at_the_same_start_time_is_rejected()
    {
        var tenantId = Guid.NewGuid();
        var tenant = new MutableTenantContext();
        tenant.ScopeTo(tenantId);
        var clock = new TestClock(new DateTimeOffset(2026, 6, 24, 0, 0, 0, TimeSpan.Zero));
        var user = new FakeCurrentUser { TenantId = tenantId, UserId = Guid.NewGuid() };
        await using var db = InMemoryDb.Create(tenant);
        var location = new ParkingLocation(tenantId, "Lot", "lot", "Asia/Manila", null);
        db.ParkingLocations.Add(location);
        await db.SaveChangesAsync();

        var service = new CorporateBenefitService(db, user, clock, new PlateNormalizer(), new AuditLogger(db, user, clock));
        var request = Request(location.Id);
        var program = await service.CreateAsync(request, CancellationToken.None);
        var effectiveFrom = clock.UtcNow.AddDays(1);
        var future = new UpdateCorporateBenefitRequest(
            request.Name,
            request.Description,
            request.Priority,
            request.Locations,
            request.Rules,
            request.PlateNumbers,
            effectiveFrom,
            request.EffectiveTo);

        await service.UpdateAsync(program.Id, future, CancellationToken.None);

        var act = () => service.UpdateAsync(program.Id, future, CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("A future benefit revision is already scheduled. Edit or wait for that revision before adding another change.");
    }

    private static CreateCorporateBenefitRequest Request(Guid locationId)
        => new(
            "ABC Corporation",
            "",
            0,
            new[] { new CorporateBenefitLocationRequest(locationId, 1) },
            new CorporateBenefitRulesRequest(
                new[] { new CorporateBenefitWindowRequest("08:00", "20:00") },
                new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" },
                Array.Empty<string>(),
                false,
                Array.Empty<string>()),
            new[] { "ABC123" });
}
