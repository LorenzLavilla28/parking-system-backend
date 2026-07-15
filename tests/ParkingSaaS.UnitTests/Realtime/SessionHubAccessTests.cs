using FluentAssertions;
using ParkingSaaS.Api.Realtime;
using Xunit;

namespace ParkingSaaS.UnitTests.Realtime;

public sealed class SessionHubAccessTests
{
    [Fact]
    public void Group_names_are_prefixed_and_stable()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        SessionGroups.Location(id).Should().Be("location:11111111-1111-1111-1111-111111111111");
        SessionGroups.Tenant(id).Should().Be("tenant:11111111-1111-1111-1111-111111111111");
    }

    [Theory]
    // supervisor/admin: any location within their tenant.
    [InlineData(true, true, false, true)]
    // supervisor/admin but the location is not in their tenant → denied.
    [InlineData(true, false, false, false)]
    // guard assigned to the location → allowed.
    [InlineData(false, true, true, true)]
    // guard not assigned to the location → denied.
    [InlineData(false, true, false, false)]
    // guard assigned, but location outside tenant → denied.
    [InlineData(false, false, true, false)]
    public void CanJoinLocation_enforces_tenant_and_assignment(
        bool isSupervisorOrAdmin, bool locationInTenant, bool isAssignedGuard, bool expected)
    {
        SessionHubAccess.CanJoinLocation(isSupervisorOrAdmin, locationInTenant, isAssignedGuard)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(true, true)]   // supervisors/admins may watch the whole tenant.
    [InlineData(false, false)] // plain guards may not.
    public void CanJoinTenant_is_supervisor_only(bool isSupervisorOrAdmin, bool expected)
        => SessionHubAccess.CanJoinTenant(isSupervisorOrAdmin).Should().Be(expected);
}
