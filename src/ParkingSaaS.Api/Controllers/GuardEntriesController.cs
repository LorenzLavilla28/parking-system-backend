using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkingSaaS.Api.Auth;
using ParkingSaaS.Application.Guard;
using ParkingSaaS.Contracts.Common;
using ParkingSaaS.Contracts.Guard;

namespace ParkingSaaS.Api.Controllers;

/// <summary>Guard-facing vehicle entry. Returns a one-time printable QR ticket.</summary>
[Authorize(Policy = AuthorizationPolicies.GuardOrAbove)]
[Route("api/guard/entries")]
public sealed class GuardEntriesController : ApiControllerBase
{
    private readonly IGuardEntryService _entries;

    public GuardEntriesController(IGuardEntryService entries) => _entries = entries;

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<EntryTicketResponse>), StatusCodes.Status201Created)]
    public async Task<IActionResult> Record([FromBody] RecordEntryRequest request, CancellationToken ct)
    {
        var ticket = await _entries.RecordEntryAsync(request, ct);
        return CreatedAtRoute("GuardSessionsGet", new { id = ticket.SessionId },
            ApiResponse<EntryTicketResponse>.Ok(ticket));
    }
}
