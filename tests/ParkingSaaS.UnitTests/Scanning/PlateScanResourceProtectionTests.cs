using System.Reflection;
using System.Threading.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.RateLimiting;
using ParkingSaaS.Api.Controllers;
using ParkingSaaS.Api.RateLimiting;
using ParkingSaaS.Application.Common.Options;
using ParkingSaaS.Infrastructure.Scanning;
using SkiaSharp;

namespace ParkingSaaS.UnitTests.Scanning;

public sealed class PlateScanResourceProtectionTests
{
    [Theory]
    [InlineData(4032, 3024, 1920, 1920, 1440)]
    [InlineData(3024, 4032, 1920, 1440, 1920)]
    [InlineData(1200, 675, 1920, 1200, 675)]
    public void CalculateInferenceSize_preserves_aspect_ratio_without_upscaling(
        int width,
        int height,
        int maxDimension,
        int expectedWidth,
        int expectedHeight)
    {
        var result = YoloPlateRegionDetector.CalculateInferenceSize(width, height, maxDimension);

        result.Should().Be((expectedWidth, expectedHeight));
    }

    [Fact]
    public void DownscaleIfNeeded_resizes_oversized_bitmap()
    {
        using var source = new SKBitmap(400, 300);

        using var result = YoloPlateRegionDetector.DownscaleIfNeeded(source, 192);

        result.Should().NotBeNull();
        result!.Width.Should().Be(192);
        result.Height.Should().Be(144);
    }

    [Fact]
    public void DownscaleIfNeeded_does_not_enlarge_small_bitmap()
    {
        using var source = new SKBitmap(160, 90);

        var result = YoloPlateRegionDetector.DownscaleIfNeeded(source, 1920);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Limiter_allows_one_active_two_queued_and_rejects_the_fourth()
    {
        using var limiter = new ConcurrencyLimiter(PlateScanRateLimitPolicy.CreateOptions(new PlateScanningOptions
        {
            MaxConcurrentScans = 1,
            MaxQueuedScans = 2,
        }));

        var active = limiter.AttemptAcquire();
        active.IsAcquired.Should().BeTrue();

        var queuedFirstTask = limiter.AcquireAsync().AsTask();
        var queuedSecondTask = limiter.AcquireAsync().AsTask();
        queuedFirstTask.IsCompleted.Should().BeFalse();
        queuedSecondTask.IsCompleted.Should().BeFalse();

        using var rejected = await limiter.AcquireAsync();
        rejected.IsAcquired.Should().BeFalse();

        active.Dispose();
        var queuedFirst = await queuedFirstTask.WaitAsync(TimeSpan.FromSeconds(1));
        queuedFirst.IsAcquired.Should().BeTrue();
        queuedFirst.Dispose();

        var queuedSecond = await queuedSecondTask.WaitAsync(TimeSpan.FromSeconds(1));
        queuedSecond.IsAcquired.Should().BeTrue();
        queuedSecond.Dispose();
    }

    [Fact]
    public void Scan_endpoint_uses_bounded_concurrency_policy()
    {
        var scanMethod = typeof(GuardPlateScanController).GetMethod(nameof(GuardPlateScanController.Scan));

        var attribute = scanMethod!.GetCustomAttribute<EnableRateLimitingAttribute>();

        attribute.Should().NotBeNull();
        attribute!.PolicyName.Should().Be(PlateScanRateLimitPolicy.Name);
    }
}
