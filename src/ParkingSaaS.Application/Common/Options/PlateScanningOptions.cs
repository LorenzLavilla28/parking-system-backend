namespace ParkingSaaS.Application.Common.Options;

/// <summary>Configuration for the server-side YOLO license-plate detector.</summary>
public sealed class PlateScanningOptions
{
    public const string SectionName = "PlateScanning";

    /// <summary>Relative to the API application directory unless an absolute path is supplied.</summary>
    public string ModelPath { get; set; } = "Models/LicensePlateDetector_YOLOv8n.onnx";

    /// <summary>Lowering this slightly helps with distant plates; OCR and user confirmation remain the final gate.</summary>
    public double ConfidenceThreshold { get; set; } = 0.12;

    public double IouThreshold { get; set; } = 0.7;
    public double CropPaddingRatio { get; set; } = 0.35;
    public double MinimumCropAspectRatio { get; set; } = 3.2;
    public int MaxDetectionCandidates { get; set; } = 3;
    public long MaxImageBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Maximum width or height used for inference. Larger camera images are
    /// downscaled without changing their aspect ratio; smaller images are not enlarged.
    /// </summary>
    public int MaxImageDimension { get; set; } = 1920;

    /// <summary>Maximum number of scans allowed to execute inference simultaneously.</summary>
    public int MaxConcurrentScans { get; set; } = 1;

    /// <summary>Maximum number of scans allowed to wait for the inference slot.</summary>
    public int MaxQueuedScans { get; set; } = 2;

    /// <summary>Directory containing the local PP-OCRv5 detection and English recognition models.</summary>
    public string PaddleModelPath { get; set; } = "Models/PaddleOCR";

    public double OcrConfidenceThreshold { get; set; } = 0.80;
}
