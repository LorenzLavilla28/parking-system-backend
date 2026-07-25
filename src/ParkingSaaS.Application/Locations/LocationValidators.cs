using FluentValidation;
using ParkingSaaS.Contracts.Locations;

namespace ParkingSaaS.Application.Locations;

internal static class SlugRules
{
    // lowercase letters, digits, hyphens; no leading/trailing/double hyphen.
    public const string Pattern = "^[a-z0-9]+(?:-[a-z0-9]+)*$";
}

public sealed class CreateLocationRequestValidator : AbstractValidator<CreateLocationRequest>
{
    public CreateLocationRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(80)
            .Matches(SlugRules.Pattern).WithMessage("Slug must be lowercase alphanumeric words separated by hyphens.");
        RuleFor(x => x.Timezone).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.SlotCapacity).InclusiveBetween(1, 100000);
    }
}

public sealed class UpdateLocationRequestValidator : AbstractValidator<UpdateLocationRequest>
{
    public UpdateLocationRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Timezone).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.SlotCapacity).InclusiveBetween(1, 100000);
    }
}
