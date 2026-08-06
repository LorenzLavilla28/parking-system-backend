namespace ParkingSaaS.Application.Abstractions;

/// <summary>Locates license-plate regions in an uploaded image.</summary>
public interface IPlateRegionDetector
{
    Task<IReadOnlyList<PlateRegionDetection>> DetectAsync(Stream image, CancellationToken cancellationToken = default);
}

public sealed record PlateRegionDetection(
    double Confidence,
    int ImageWidth,
    int ImageHeight,
    int Left,
    int Top,
    int Width,
    int Height,
    byte[] CroppedImageBytes);

public interface IPlateTextRecognizer
{
    Task<PlateTextRecognition?> RecognizeAsync(byte[] image, CancellationToken cancellationToken = default);
}

public sealed record PlateTextRecognition(string PlateNumber, double Confidence, int ShapeScore);
