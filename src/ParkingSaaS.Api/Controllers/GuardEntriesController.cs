using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkingSaaS.Api.Auth;
using ParkingSaaS.Application.Guard;
using ParkingSaaS.Application.Benefits;
using ParkingSaaS.Contracts.Benefits;
using ParkingSaaS.Contracts.Common;
using ParkingSaaS.Contracts.Guard;

namespace ParkingSaaS.Api.Controllers;

/// <summary>Guard-facing vehicle entry. Returns a one-time printable QR ticket.</summary>
[Authorize(Policy = AuthorizationPolicies.GuardOrAbove)]
[Route("api/guard/entries")]
public sealed class GuardEntriesController : ApiControllerBase
{
    private readonly IGuardEntryService _entries;
    private readonly ICorporateBenefitService _benefits;

    public GuardEntriesController(IGuardEntryService entries, ICorporateBenefitService benefits)
    {
        _entries = entries;
        _benefits = benefits;
    }

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<EntryTicketResponse>), StatusCodes.Status201Created)]
    public async Task<IActionResult> Record([FromBody] RecordEntryRequest request, CancellationToken ct)
    {
        var ticket = await _entries.RecordEntryAsync(request, ct);
        return CreatedAtRoute("GuardSessionsGet", new { id = ticket.SessionId },
            ApiResponse<EntryTicketResponse>.Ok(ticket));
    }

    [HttpGet("corporate-benefits")]
    public async Task<IActionResult> CorporateBenefits(Guid locationId, string vehicleType, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<GuardCorporateBenefitOptionResponse>>.Ok(
            await _benefits.ListGuardOptionsAsync(locationId, vehicleType, ct)));
}
