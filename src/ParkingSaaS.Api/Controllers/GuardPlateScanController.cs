using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ParkingSaaS.Api.Auth;
using ParkingSaaS.Api.RateLimiting;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Options;
using ParkingSaaS.Contracts.Common;
using ParkingSaaS.Contracts.Guard;

namespace ParkingSaaS.Api.Controllers;

/// <summary>Server-side plate localization for guard camera and upload scans.</summary>
[Authorize(Policy = AuthorizationPolicies.GuardOrAbove)]
[Route("api/guard/plate-scan")]
public sealed class GuardPlateScanController : ApiControllerBase
{
    private readonly IPlateRegionDetector _detector;
    private readonly IPlateTextRecognizer _recognizer;
    private readonly PlateScanningOptions _options;

    public GuardPlateScanController(
        IPlateRegionDetector detector,
        IPlateTextRecognizer recognizer,
        IOptions<PlateScanningOptions> options)
    {
        _detector = detector;
        _recognizer = recognizer;
        _options = options.Value;
    }

    [HttpPost]
    [EnableRateLimiting(PlateScanRateLimitPolicy.Name)]
    [RequestSizeLimit(25 * 1024 * 1024)]
    [ProducesResponseType(typeof(ApiResponse<PlateScanResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Scan([FromForm] IFormFile? image, CancellationToken ct)
    {
        if (image is null || image.Length == 0)
            return BadRequest(new { message = "An image file is required." });

        if (image.Length > _options.MaxImageBytes)
            return BadRequest(new { message = $"The image must be {_options.MaxImageBytes / (1024 * 1024)} MiB or smaller." });

        await using var stream = image.OpenReadStream();
        var proposals = await _detector.DetectAsync(stream, ct);
        PlateRegionDetection? selectedDetection = null;
        PlateTextRecognition? selectedRecognition = null;

        foreach (var proposal in proposals)
        {
            var recognition = await _recognizer.RecognizeAsync(proposal.CroppedImageBytes, ct);
            if (recognition is null || !IsBetter(recognition, proposal, selectedRecognition, selectedDetection))
                continue;

            selectedDetection = proposal;
            selectedRecognition = recognition;

            // A strong recognized format inside a YOLO-localized crop does not
            // need the slower full-frame OCR proposal.
            if (recognition.ShapeScore >= 6 && proposal.Confidence > 0)
                break;
        }

        var fallbackDetection = selectedDetection ?? proposals.FirstOrDefault(proposal => proposal.Confidence > 0);
        var response = fallbackDetection is null
            ? new PlateScanResponse(false, null, null, null, null, null, null)
            : new PlateScanResponse(
                selectedRecognition is not null,
                selectedRecognition?.PlateNumber,
                fallbackDetection.Confidence > 0 ? fallbackDetection.Confidence : null,
                selectedRecognition?.Confidence,
                new PlateScanBoundingBox(
                    fallbackDetection.Left,
                    fallbackDetection.Top,
                    fallbackDetection.Width,
                    fallbackDetection.Height),
                fallbackDetection.ImageWidth,
                fallbackDetection.ImageHeight);

        return Ok(ApiResponse<PlateScanResponse>.Ok(response));
    }

    private static bool IsBetter(
        PlateTextRecognition candidate,
        PlateRegionDetection candidateDetection,
        PlateTextRecognition? current,
        PlateRegionDetection? currentDetection)
    {
        if (current is null)
            return true;
        if (candidate.ShapeScore != current.ShapeScore)
            return candidate.ShapeScore > current.ShapeScore;
        if (candidate.PlateNumber.Length != current.PlateNumber.Length)
            return candidate.PlateNumber.Length > current.PlateNumber.Length;
        if (Math.Abs(candidateDetection.Confidence - (currentDetection?.Confidence ?? 0)) > 0.001)
            return candidateDetection.Confidence > (currentDetection?.Confidence ?? 0);
        if (Math.Abs(candidate.Confidence - current.Confidence) > 0.001)
            return candidate.Confidence > current.Confidence;
        return false;
    }
}
