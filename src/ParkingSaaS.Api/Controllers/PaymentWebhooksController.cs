using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkingSaaS.Application.Payments;

namespace ParkingSaaS.Api.Controllers;

/// <summary>
/// Receives PayMongo webhooks. Reads the raw request body (so the signature can be
/// verified byte-for-byte) and never trusts the redirect/browser flow. Returns 200
/// for anything successfully stored/processed so the provider stops retrying;
/// 400 for an invalid signature; 500 only for transient errors worth retrying.
/// </summary>
[AllowAnonymous]
[Route("api/payments/webhooks")]
public sealed class PaymentWebhooksController : ApiControllerBase
{
    private readonly IPaymentWebhookService _webhooks;

    public PaymentWebhooksController(IPaymentWebhookService webhooks) => _webhooks = webhooks;

    [HttpPost("paymongo")]
    public async Task<IActionResult> PayMongo(CancellationToken ct)
    {
        // Preserve the exact bytes for signature verification.
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(ct);

        var signature = Request.Headers["Paymongo-Signature"].ToString();

        var outcome = await _webhooks.ProcessPayMongoAsync(rawBody, signature, ct);
        return outcome switch
        {
            WebhookOutcome.InvalidSignature => BadRequest(new { error = "invalid_signature" }),
            WebhookOutcome.TransientError => StatusCode(StatusCodes.Status500InternalServerError),
            _ => Ok(new { received = true })
        };
    }
}
