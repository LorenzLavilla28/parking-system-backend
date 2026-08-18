using FluentValidation;
using ParkingSaaS.Contracts.Guard;

namespace ParkingSaaS.Application.Guard;

public sealed class RecordEntryRequestValidator : AbstractValidator<RecordEntryRequest>
{
    public RecordEntryRequestValidator()
    {
        RuleFor(x => x.ParkingLocationId).NotEmpty();
        RuleFor(x => x.PlateNumber).NotEmpty().MaximumLength(32);
        RuleFor(x => x.VehicleType).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.EntryPhotoUrl).MaximumLength(1000);
    }
}
