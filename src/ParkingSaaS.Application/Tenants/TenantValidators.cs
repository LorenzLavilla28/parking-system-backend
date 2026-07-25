using FluentValidation;
using ParkingSaaS.Contracts.Tenants;

namespace ParkingSaaS.Application.Tenants;

public sealed class CreateTenantRequestValidator : AbstractValidator<CreateTenantRequest>
{
    public CreateTenantRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(80)
            .Matches("^[a-z0-9]+(?:-[a-z0-9]+)*$")
            .WithMessage("Slug must be lowercase alphanumeric words separated by hyphens.");
        RuleFor(x => x.SubscriptionPlan).NotEmpty();
        RuleFor(x => x.DefaultCurrency).NotEmpty().Length(3);
        RuleFor(x => x.DefaultTimezone).NotEmpty().MaximumLength(64);
        RuleFor(x => x.AdminFirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AdminLastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AdminEmail).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.AdminPassword).NotEmpty().MinimumLength(10).MaximumLength(256);
    }
}

public sealed class UpdateTenantStatusRequestValidator : AbstractValidator<UpdateTenantStatusRequest>
{
    public UpdateTenantStatusRequestValidator()
        => RuleFor(x => x.Status).NotEmpty();
}
