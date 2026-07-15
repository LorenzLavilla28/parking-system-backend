using FluentValidation;
using ParkingSaaS.Contracts.Guard;

namespace ParkingSaaS.Application.Guard;

public sealed class ApproveExitRequestValidator : AbstractValidator<ApproveExitRequest>
{
    public ApproveExitRequestValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.ExitPhotoUrl).MaximumLength(1000);
        RuleFor(x => x.DeviceInformation).MaximumLength(256);
        RuleFor(x => x.OverrideReason).MaximumLength(500);
    }
}

public sealed class RecordCashPaymentRequestValidator : AbstractValidator<RecordCashPaymentRequest>
{
    public RecordCashPaymentRequestValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.AmountReceived).GreaterThan(0m);
        RuleFor(x => x.DeviceInformation).MaximumLength(256);
    }
}
