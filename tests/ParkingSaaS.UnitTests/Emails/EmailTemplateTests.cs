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

    [Fact]
    public void Payment_receipt_contains_sales_invoice_request_instructions()
    {
        var email = EmailTemplates.PaymentReceipt(
            Guid.NewGuid(),
            "customer@example.com",
            new PaymentReceiptEmailData(
                "ABC1234",
                "Main Street Parking",
                180m,
                "PHP",
                new DateTimeOffset(2026, 8, 6, 10, 0, 0, TimeSpan.Zero),
                "gcash",
                "PAY-123",
                null),
            new DateTimeOffset(2026, 8, 6, 10, 1, 0, TimeSpan.Zero),
            3);

        email.HtmlBody.Should().Contain("mailto:info@julicis.com");
        email.HtmlBody.Should().Contain("To request a sales invoice");
        email.TextBody.Should().Contain("To request a sales invoice, please forward this email to info@julicis.com.");
    }
}
