using ParkingSaaS.Contracts.Guard;

namespace ParkingSaaS.Application.Guard;

public interface IGuardEntryService
{
    Task<EntryTicketResponse> RecordEntryAsync(RecordEntryRequest request, CancellationToken ct);
}
