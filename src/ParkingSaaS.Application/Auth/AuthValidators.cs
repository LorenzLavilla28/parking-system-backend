using FluentValidation;
using ParkingSaaS.Contracts.Auth;

namespace ParkingSaaS.Application.Auth;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(256);
    }
}

public sealed class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator()
        => RuleFor(x => x.RefreshToken).NotEmpty();
}

public sealed class LogoutRequestValidator : AbstractValidator<LogoutRequest>
{
    public LogoutRequestValidator()
        => RuleFor(x => x.RefreshToken).NotEmpty();
}

public sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator()
        => RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(12).MaximumLength(256)
            .WithMessage("Password must be at least 12 characters.")
            .Matches("\\d").WithMessage("Password must contain a number.")
            .Matches("[^A-Za-z0-9]").WithMessage("Password must contain a special character.");
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(256);
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(12).MaximumLength(256)
            .WithMessage("Password must be at least 12 characters.")
            .Matches("\\d").WithMessage("Password must contain a number.")
            .Matches("[^A-Za-z0-9]").WithMessage("Password must contain a special character.");
        RuleFor(x => x)
            .Must(x => x.CurrentPassword != x.NewPassword)
            .WithMessage("New password must be different from the current password.");
    }
}
