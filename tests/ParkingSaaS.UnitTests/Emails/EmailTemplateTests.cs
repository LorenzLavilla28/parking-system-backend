using FluentAssertions;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Emails;
using ParkingSaaS.Domain.Emails;
using Xunit;

namespace ParkingSaaS.UnitTests.Emails;

public sealed class EmailTemplateTests
{
    [Fact]
    public void Overstay_notice_contains_a_payment_link_and_qr_code()
    {
        var email = EmailTemplates.OverstayNotice(
            Guid.NewGuid(),
            "customer@example.com",
            new OverstayNoticeEmailData(
                "ABC1234",
                "Main Street Parking",
                new DateTimeOffset(2026, 7, 26, 10, 0, 0, TimeSpan.Zero),
                "https://parking.test/p/session-token",
                "data:image/png;base64,qr-data"),
            new DateTimeOffset(2026, 7, 26, 10, 1, 0, TimeSpan.Zero),
            3);

        email.Kind.Should().Be(EmailKind.OverstayNotice);
        email.HtmlBody.Should().Contain("https://parking.test/p/session-token");
        email.HtmlBody.Should().Contain("data:image/png;base64,qr-data");
        email.TextBody.Should().Contain("Review and pay: https://parking.test/p/session-token");
    }
}
