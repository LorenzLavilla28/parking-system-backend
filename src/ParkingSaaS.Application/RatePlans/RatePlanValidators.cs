using FluentValidation;
using ParkingSaaS.Contracts.RatePlans;

namespace ParkingSaaS.Application.RatePlans;

public sealed class CreateRatePlanRequestValidator : AbstractValidator<CreateRatePlanRequest>
{
    public CreateRatePlanRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(500);
        RuleFor(x => x.RulesJson).NotEmpty();
    }
}

public sealed class AddRatePlanVersionRequestValidator : AbstractValidator<AddRatePlanVersionRequest>
{
    public AddRatePlanVersionRequestValidator()
        => RuleFor(x => x.RulesJson).NotEmpty();
}
