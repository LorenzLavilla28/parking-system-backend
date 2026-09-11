using FluentValidation;
using ParkingSaaS.Contracts.Platform;

namespace ParkingSaaS.Application.Platform;

public sealed class InvitePlatformAdministratorRequestValidator
    : AbstractValidator<InvitePlatformAdministratorRequest>
{
    public InvitePlatformAdministratorRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
    }
}
