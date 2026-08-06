using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCvSharp;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Options;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models;

namespace ParkingSaaS.Infrastructure.Scanning;

/// <summary>Backend-only PP-OCRv5 recognition for a YOLO-localized plate crop.</summary>
public sealed class PaddleOcrPlateTextRecognizer : IPlateTextRecognizer, IDisposable
{
    private readonly PlateScanningOptions _options;
    private readonly ILogger<PaddleOcrPlateTextRecognizer> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lazy<PaddleOcrAll> _ocr;

    public PaddleOcrPlateTextRecognizer(
        IOptions<PlateScanningOptions> options,
        ILogger<PaddleOcrPlateTextRecognizer> logger)
    {
        _options = options.Value;
        _logger = logger;
        _ocr = new Lazy<PaddleOcrAll>(CreateOcr, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async Task<PlateTextRecognition?> RecognizeAsync(
        byte[] image,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var source = Cv2.ImDecode(image, ImreadModes.Color);
            if (source.Empty())
                throw new InvalidDataException("The localized plate crop could not be decoded for OCR.");

            var result = _ocr.Value.Run(source);
            var best = PhilippinePlateCandidateSelector.SelectBest(
                result.Regions.Select(ToTextRegion),
                _options.OcrConfidenceThreshold);

            if (best is null)
                return null;

            _logger.LogDebug(
                "PaddleOCR recognized plate {PlateNumber} with confidence {Confidence}.",
                best.PlateNumber,
                best.Confidence);
            return new PlateTextRecognition(best.PlateNumber, best.Confidence, best.ShapeScore);
        }
        finally
        {
            _gate.Release();
        }
    }

    private PaddleOcrAll CreateOcr()
    {
        var modelRoot = Path.IsPathRooted(_options.PaddleModelPath)
            ? _options.PaddleModelPath
            : Path.Combine(AppContext.BaseDirectory, _options.PaddleModelPath.Replace('/', Path.DirectorySeparatorChar));
        var detectionPath = Path.Combine(modelRoot, "PP-OCRv5_mobile_det_infer");
        var recognitionPath = Path.Combine(modelRoot, "en_PP-OCRv5_mobile_rec");

        ValidateModelDirectory(detectionPath);
        ValidateModelDirectory(recognitionPath);

        _logger.LogInformation("Loading local PaddleOCR models from {ModelPath}.", modelRoot);
        var model = new FullOcrModel(
            DetectionModel.FromDirectory(detectionPath, ModelVersion.V5),
            RecognizationModel.FromDirectoryV5(recognitionPath));
        var device = OperatingSystem.IsWindows()
            ? PaddleDevice.OneDnn()
            : PaddleDevice.Blas();

        return new PaddleOcrAll(model, device)
        {
            AllowRotateDetection = false,
            Enable180Classification = false,
        };
    }

    private static void ValidateModelDirectory(string path)
    {
        if (!Directory.Exists(path) ||
            !File.Exists(Path.Combine(path, "inference.json")) ||
            !File.Exists(Path.Combine(path, "inference.pdiparams")))
        {
            throw new DirectoryNotFoundException(
                $"A complete PaddleOCR model was not found at '{path}'. Set PlateScanning:PaddleModelPath or deploy the bundled models.");
        }
    }

    private static PlateOcrTextRegion ToTextRegion(PaddleOcrResultRegion region)
    {
        var points = region.Rect.Points();
        var characters = region.Chars
            .SelectMany(character => character.Character
                .ToUpperInvariant()
                .Where(char.IsAsciiLetterOrDigit)
                .Select(value => new PlateOcrCharacter(value, character.Score)))
            .ToArray();
        return new PlateOcrTextRegion(
            region.Text,
            region.Score,
            characters,
            points.Min(point => point.X),
            points.Min(point => point.Y),
            points.Max(point => point.X),
            points.Max(point => point.Y));
    }

    public void Dispose()
    {
        if (_ocr.IsValueCreated)
            _ocr.Value.Dispose();
        _gate.Dispose();
    }
}
