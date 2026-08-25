using FluentValidation;
using ParkingSaaS.Contracts.Benefits;

namespace ParkingSaaS.Application.Benefits;

public sealed class CreateCorporateBenefitRequestValidator : AbstractValidator<CreateCorporateBenefitRequest>
{
    public CreateCorporateBenefitRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(160);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.Priority).InclusiveBetween(0, 10000);
        RuleFor(x => x.Locations).NotEmpty();
        RuleForEach(x => x.Locations).SetValidator(new CorporateBenefitLocationRequestValidator());
    }
}

public sealed class UpdateCorporateBenefitRequestValidator : AbstractValidator<UpdateCorporateBenefitRequest>
{
    public UpdateCorporateBenefitRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(160);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.Priority).InclusiveBetween(0, 10000);
        RuleFor(x => x.Locations).NotEmpty();
        RuleForEach(x => x.Locations).SetValidator(new CorporateBenefitLocationRequestValidator());
    }
}

public sealed class CorporateBenefitLocationRequestValidator : AbstractValidator<CorporateBenefitLocationRequest>
{
    public CorporateBenefitLocationRequestValidator()
        => RuleFor(x => x.MaxConcurrentFreeSessions).InclusiveBetween(1, 100000);
}

public sealed class SetCorporateBenefitStatusRequestValidator : AbstractValidator<SetCorporateBenefitStatusRequest>
{
    public SetCorporateBenefitStatusRequestValidator()
        => RuleFor(x => x.Status).NotEmpty().Must(value => new[] { "Draft", "Active", "Paused", "Archived" }.Contains(value, StringComparer.OrdinalIgnoreCase));
}
