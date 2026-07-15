using FluentValidation;
using ParkingSaaS.Contracts.Supervisor;

namespace ParkingSaaS.Application.Supervisor;

public sealed class VoidSessionRequestValidator : AbstractValidator<VoidSessionRequest>
{
    public VoidSessionRequestValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class ComplimentaryRequestValidator : AbstractValidator<ComplimentaryRequest>
{
    public ComplimentaryRequestValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class WaiveOutstandingRequestValidator : AbstractValidator<WaiveOutstandingRequest>
{
    public WaiveOutstandingRequestValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class CorrectEntryTimeRequestValidator : AbstractValidator<CorrectEntryTimeRequest>
{
    public CorrectEntryTimeRequestValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.NewEntryTime).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class CorrectPlateRequestValidator : AbstractValidator<CorrectPlateRequest>
{
    public CorrectPlateRequestValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.NewPlateNumber).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}
