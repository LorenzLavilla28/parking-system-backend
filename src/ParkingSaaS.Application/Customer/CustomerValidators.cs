using FluentValidation;
using ParkingSaaS.Contracts.Customer;

namespace ParkingSaaS.Application.Customer;

public sealed class PlateLookupRequestValidator : AbstractValidator<PlateLookupRequest>
{
    public PlateLookupRequestValidator()
    {
        RuleFor(x => x.PlateNumber).NotEmpty().MaximumLength(32);
        RuleFor(x => x.CaptchaToken).MaximumLength(2000);
    }
}

public sealed class CreateFeeQuoteRequestValidator : AbstractValidator<CreateFeeQuoteRequest>
{
    public CreateFeeQuoteRequestValidator()
        => RuleFor(x => x.PublicToken).NotEmpty().MaximumLength(200);
}

public sealed class StartCheckoutRequestValidator : AbstractValidator<StartCheckoutRequest>
{
    public StartCheckoutRequestValidator()
    {
        RuleFor(x => x.FeeQuoteId).NotEmpty();
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email)).MaximumLength(256);
    }
}
