using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Options;
using SkiaSharp;
using YoloDotNet;
using YoloDotNet.ExecutionProvider.Cpu;
using YoloDotNet.Models;

namespace ParkingSaaS.Infrastructure.Scanning;

/// <summary>
/// CPU-backed YoloDotNet plate localization. The model is loaded once and inference
/// is serialized because the ONNX session is shared by all guard requests.
/// </summary>
public sealed class YoloPlateRegionDetector : IPlateRegionDetector, IDisposable
{
    private readonly PlateScanningOptions _options;
    private readonly ILogger<YoloPlateRegionDetector> _logger;
    private readonly object _sync = new();
    private readonly Lazy<Yolo> _yolo;

    public YoloPlateRegionDetector(
        IOptions<PlateScanningOptions> options,
        ILogger<YoloPlateRegionDetector> logger)
    {
        _options = options.Value;
        _logger = logger;
        _yolo = new Lazy<Yolo>(CreateYolo, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async Task<IReadOnlyList<PlateRegionDetection>> DetectAsync(
        Stream image,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        cancellationToken.ThrowIfCancellationRequested();

        // Decode once before entering inference. The stream belongs to the request and
        // may be disposed as soon as the endpoint returns.
        using var input = new MemoryStream();
        await image.CopyToAsync(input, cancellationToken);
        input.Position = 0;

        using var decoded = SKBitmap.Decode(input)
            ?? throw new InvalidDataException("The uploaded file is not a supported image.");
        using var downscaled = DownscaleIfNeeded(decoded, _options.MaxImageDimension);
        var bitmap = downscaled ?? decoded;

        if (downscaled is not null)
        {
            _logger.LogDebug(
                "Downscaled plate scan from {OriginalWidth}x{OriginalHeight} to {Width}x{Height} for inference.",
                decoded.Width,
                decoded.Height,
                bitmap.Width,
                bitmap.Height);
        }

        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            var detections = _yolo.Value.RunObjectDetection(
                bitmap,
                confidence: _options.ConfidenceThreshold,
                iou: _options.IouThreshold);

            var selected = detections
                .OrderByDescending(result => result.Confidence)
                .Take(Math.Max(1, _options.MaxDetectionCandidates))
                .ToArray();
            var proposals = new List<PlateRegionDetection>(selected.Length + 1);

            foreach (var detection in selected)
            {
                var box = ClampAndPad(detection.BoundingBox, bitmap.Width, bitmap.Height);
                proposals.Add(CreateProposal(bitmap, box, detection.Confidence));

                _logger.LogDebug(
                    "Detected plate candidate at {Left},{Top} {Width}x{Height} with confidence {Confidence}.",
                    box.Left, box.Top, box.Width, box.Height, detection.Confidence);
            }

            // OCR on the full image is the final server-side proposal. It covers
            // Philippine plate styles that the generic YOLO model may confuse with
            // a year sticker or fail to localize entirely.
            proposals.Add(CreateProposal(
                bitmap,
                new SKRectI(0, 0, bitmap.Width, bitmap.Height),
                detectorConfidence: 0));
            return proposals;
        }
    }

    public void Dispose()
    {
        if (_yolo.IsValueCreated)
            _yolo.Value.Dispose();
    }

    private Yolo CreateYolo()
    {
        var modelPath = Path.IsPathRooted(_options.ModelPath)
            ? _options.ModelPath
            : Path.Combine(AppContext.BaseDirectory, _options.ModelPath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException(
                $"License-plate detector model was not found at '{modelPath}'. Set PlateScanning:ModelPath or deploy the model asset.",
                modelPath);
        }

        _logger.LogInformation("Loading license-plate detector model from {ModelPath}.", modelPath);
        return new Yolo(new YoloOptions
        {
            ExecutionProvider = new CpuExecutionProvider(modelPath),
        });
    }

    private SKRectI ClampAndPad(SKRectI source, int imageWidth, int imageHeight)
    {
        var aspectWidth = (int)Math.Round(source.Height * _options.MinimumCropAspectRatio);
        var padX = Math.Max(
            (int)Math.Round(source.Width * _options.CropPaddingRatio),
            (int)Math.Ceiling(Math.Max(0, aspectWidth - source.Width) / 2d));
        var padY = (int)Math.Round(source.Height * _options.CropPaddingRatio);
        var left = Math.Clamp(source.Left - padX, 0, imageWidth - 1);
        var top = Math.Clamp(source.Top - padY, 0, imageHeight - 1);
        var right = Math.Clamp(source.Right + padX, left + 1, imageWidth);
        var bottom = Math.Clamp(source.Bottom + padY, top + 1, imageHeight);
        return new SKRectI(left, top, right, bottom);
    }

    private static PlateRegionDetection CreateProposal(SKBitmap bitmap, SKRectI box, double detectorConfidence)
    {
        using var crop = new SKBitmap(box.Width, box.Height);
        using (var canvas = new SKCanvas(crop))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(
                bitmap,
                new SKRect(box.Left, box.Top, box.Right, box.Bottom),
                new SKRect(0, 0, box.Width, box.Height));
        }

        using var encoded = SKImage.FromBitmap(crop).Encode(SKEncodedImageFormat.Jpeg, 94);
        return new PlateRegionDetection(
            detectorConfidence,
            bitmap.Width,
            bitmap.Height,
            box.Left,
            box.Top,
            box.Width,
            box.Height,
            encoded.ToArray());
    }

    internal static SKBitmap? DownscaleIfNeeded(SKBitmap source, int maxDimension)
    {
        var size = CalculateInferenceSize(source.Width, source.Height, maxDimension);
        if (size.Width == source.Width && size.Height == source.Height)
            return null;

        var resized = new SKBitmap(size.Width, size.Height, source.ColorType, source.AlphaType);
        if (!source.ScalePixels(resized, new SKSamplingOptions(SKCubicResampler.Mitchell)))
        {
            resized.Dispose();
            throw new InvalidOperationException("The plate scan could not be downscaled for inference.");
        }

        return resized;
    }

    internal static (int Width, int Height) CalculateInferenceSize(
        int width,
        int height,
        int maxDimension)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDimension);

        var longestSide = Math.Max(width, height);
        if (longestSide <= maxDimension)
            return (width, height);

        var scale = (double)maxDimension / longestSide;
        return (
            Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)));
    }
}
