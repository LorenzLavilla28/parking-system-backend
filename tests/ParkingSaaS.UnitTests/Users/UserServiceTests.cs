using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ParkingSaaS.Application.Common;
using ParkingSaaS.Application.Users;
using ParkingSaaS.Contracts.Users;
using ParkingSaaS.Domain.Emails;
using ParkingSaaS.Domain.Tenants;
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
}
