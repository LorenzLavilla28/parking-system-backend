using System.Text.RegularExpressions;

namespace ParkingSaaS.Infrastructure.Scanning;

/// <summary>
/// Selects plate-like text without assuming one fixed Philippine plate layout.
/// LTO series include legacy, current, temporary, motorcycle, electric, vintage,
/// and special plates, so geometry and OCR confidence are used alongside format.
/// </summary>
internal static class PhilippinePlateCandidateSelector
{
    private static readonly Regex StandardMotorVehicle = new("^[A-Z]{3}[0-9]{3,4}$", RegexOptions.Compiled);
    private static readonly Regex ReverseSeries = new("^[0-9]{3,4}[A-Z]{2,3}$", RegexOptions.Compiled);
    private static readonly Regex MotorcycleSpecialSeries = new("^[A-Z][0-9]{3}[A-Z]{2}$", RegexOptions.Compiled);
    private static readonly Regex TemporaryOrSpecialSeries = new("^[A-Z]{1,2}[0-9]{4,6}$", RegexOptions.Compiled);
    private static readonly string[] NoiseMarkers =
    [
        "REGISTERED", "TEMPORARYPLATE", "IMPROVISEDPLATE", "MVFILENO", "FILENO",
        "REGION", "NCR", "PILIPINAS", "PHILIPPINES", "MATATAG", "REPUBLIKA",
        "VINTAGEVEHICLE", "PHOTOROOM",
    ];

    public static PlateOcrCandidate? SelectBest(
        IEnumerable<PlateOcrTextRegion> regions,
        double minimumConfidence)
    {
        var fragments = regions
            .Select(CreateFragment)
            .Where(fragment => fragment is not null)
            .Cast<PlateOcrFragment>()
            .ToArray();
        var candidates = new List<PlateOcrCandidate>();

        foreach (var fragment in fragments)
            AddCandidateVariants(candidates, fragment.Characters, sourceFragmentCount: 1);

        for (var firstIndex = 0; firstIndex < fragments.Length; firstIndex++)
        {
            for (var secondIndex = firstIndex + 1; secondIndex < fragments.Length; secondIndex++)
            {
                var first = fragments[firstIndex];
                var second = fragments[secondIndex];
                if (!CanCombine(first, second))
                    continue;

                var ordered = OrderForReading(first, second);
                AddCandidateVariants(
                    candidates,
                    ordered.SelectMany(fragment => fragment.Characters).ToArray(),
                    sourceFragmentCount: 2);
            }
        }

        // Some plates are split into three OCR boxes (for example letters, a
        // separator, and digits). Keep triples constrained to a single text line.
        var horizontal = fragments.OrderBy(fragment => fragment.Left).ToArray();
        for (var index = 0; index + 2 < horizontal.Length; index++)
        {
            var first = horizontal[index];
            var second = horizontal[index + 1];
            var third = horizontal[index + 2];
            if (!IsSameLine(first, second) || !IsSameLine(second, third))
                continue;

            AddCandidateVariants(
                candidates,
                first.Characters.Concat(second.Characters).Concat(third.Characters).ToArray(),
                sourceFragmentCount: 3);
        }

        return candidates
            .Where(candidate => candidate.Confidence >= minimumConfidence)
            .GroupBy(candidate => candidate.PlateNumber, StringComparer.Ordinal)
            .Select(group => group
                .OrderBy(candidate => candidate.NormalizationEdits)
                .ThenByDescending(candidate => candidate.ShapeScore)
                .ThenByDescending(candidate => candidate.Confidence)
                .ThenByDescending(candidate => candidate.SourceFragmentCount)
                .First())
            .OrderBy(candidate => candidate.NormalizationEdits)
            .ThenByDescending(candidate => candidate.ShapeScore)
            .ThenByDescending(candidate => candidate.Confidence)
            .ThenByDescending(candidate => candidate.SourceFragmentCount)
            .FirstOrDefault();
    }

    private static PlateOcrFragment? CreateFragment(PlateOcrTextRegion region)
    {
        if (region.Confidence < 0.50f || float.IsNaN(region.Confidence))
            return null;

        var normalizedText = new string(region.Text
            .ToUpperInvariant()
            .Where(char.IsAsciiLetterOrDigit)
            .ToArray());
        if (normalizedText.Length == 0 || NoiseMarkers.Any(normalizedText.Contains))
            return null;

        var characters = region.Characters
            .Where(character => char.IsAsciiLetterOrDigit(character.Value))
            .ToArray();
        if (characters.Length == 0)
        {
            characters = normalizedText
                .Select(value => new PlateOcrCharacter(value, region.Confidence))
                .ToArray();
        }

        return new PlateOcrFragment(
            characters,
            region.Left,
            region.Top,
            region.Right,
            region.Bottom);
    }

    private static void AddCandidateVariants(
        ICollection<PlateOcrCandidate> candidates,
        IReadOnlyList<PlateOcrCharacter> characters,
        int sourceFragmentCount)
    {
        AddInterpretations(candidates, characters, sourceFragmentCount);

        // Bolts, logos, separators, and the monument on some legacy plates can
        // become one weak OCR character. Try removing only weak characters.
        for (var index = 0; index < characters.Count; index++)
        {
            if (index < 2 ||
                index > characters.Count - 3 ||
                !char.IsAsciiDigit(characters[index].Value) ||
                characters[index].Confidence >= 0.75f)
                continue;

            var reduced = characters.Where((_, candidateIndex) => candidateIndex != index).ToArray();
            AddInterpretations(candidates, reduced, sourceFragmentCount);
        }
    }

    private static void AddInterpretations(
        ICollection<PlateOcrCandidate> candidates,
        IReadOnlyList<PlateOcrCharacter> characters,
        int sourceFragmentCount)
    {
        var direct = BuildCandidate(characters, normalizationEdits: 0, sourceFragmentCount);
        if (direct is not null)
            candidates.Add(direct);

        // Do not reinterpret a value that already matches a recognized LTO series.
        // This preserves temporary plates such as GA01176 instead of forcing them
        // into the three-letter standard motor-vehicle layout.
        if (direct?.ShapeScore >= 5)
            return;

        if (characters.Count is not (6 or 7))
            return;

        var normalized = characters.ToArray();
        var edits = 0;
        for (var index = 0; index < 3; index++)
        {
            var value = AsLetter(normalized[index].Value);
            if (value != normalized[index].Value) edits++;
            normalized[index] = normalized[index] with { Value = value };
        }
        for (var index = 3; index < normalized.Length; index++)
        {
            var value = AsDigit(normalized[index].Value);
            if (value != normalized[index].Value) edits++;
            normalized[index] = normalized[index] with { Value = value };
        }

        if (edits == 0)
            return;

        var interpreted = BuildCandidate(normalized, edits, sourceFragmentCount);
        if (interpreted is not null)
            candidates.Add(interpreted);
    }

    private static PlateOcrCandidate? BuildCandidate(
        IReadOnlyList<PlateOcrCharacter> characters,
        int normalizationEdits,
        int sourceFragmentCount)
    {
        if (characters.Count is < 4 or > 10)
            return null;

        var plate = new string(characters.Select(character => char.ToUpperInvariant(character.Value)).ToArray());
        var letterCount = plate.Count(char.IsAsciiLetter);
        var digitCount = plate.Count(char.IsAsciiDigit);
        if (digitCount == 0)
            return null;

        var shapeScore = GetShapeScore(plate, letterCount, digitCount);
        if (shapeScore == 0)
            return null;

        var confidence = characters.Average(character => (double)character.Confidence);
        if (double.IsNaN(confidence))
            return null;

        return new PlateOcrCandidate(
            plate,
            confidence,
            shapeScore,
            normalizationEdits,
            sourceFragmentCount);
    }

    private static int GetShapeScore(string plate, int letterCount, int digitCount)
    {
        if (StandardMotorVehicle.IsMatch(plate) ||
            ReverseSeries.IsMatch(plate) ||
            MotorcycleSpecialSeries.IsMatch(plate))
            return 6;

        if (TemporaryOrSpecialSeries.IsMatch(plate))
            return 5;

        if (letterCount > 0 && digitCount > 0 && plate.Length is >= 5 and <= 8)
            return CountTypeTransitions(plate) >= 2 ? 4 : 3;

        // Numeric-only diplomatic/special plates exist, but years and long MV
        // file/registration serials should not outrank a mixed plate candidate.
        if (letterCount == 0 && plate.Length is >= 4 and <= 6 && !LooksLikeYear(plate))
            return 2;

        return letterCount > 0 && digitCount > 0 && plate.Length <= 10 ? 1 : 0;
    }

    private static int CountTypeTransitions(string value)
    {
        var transitions = 0;
        for (var index = 1; index < value.Length; index++)
        {
            if (char.IsAsciiLetter(value[index]) != char.IsAsciiLetter(value[index - 1]))
                transitions++;
        }
        return transitions;
    }

    private static bool LooksLikeYear(string value) =>
        value.Length == 4 && int.TryParse(value, out var number) && number is >= 1900 and <= 2100;

    private static bool CanCombine(PlateOcrFragment first, PlateOcrFragment second) =>
        IsSameLine(first, second) || IsStacked(first, second);

    private static bool IsSameLine(PlateOcrFragment first, PlateOcrFragment second)
    {
        var overlap = Math.Max(0, Math.Min(first.Bottom, second.Bottom) - Math.Max(first.Top, second.Top));
        var minimumHeight = Math.Max(1, Math.Min(first.Height, second.Height));
        var gap = HorizontalGap(first, second);
        var maximumGap = Math.Max(12, Math.Max(first.Height, second.Height) * 4);
        return overlap / minimumHeight >= 0.25f && gap <= maximumGap;
    }

    private static bool IsStacked(PlateOcrFragment first, PlateOcrFragment second)
    {
        var overlap = Math.Max(0, Math.Min(first.Right, second.Right) - Math.Max(first.Left, second.Left));
        var minimumWidth = Math.Max(1, Math.Min(first.Width, second.Width));
        var verticalGap = VerticalGap(first, second);
        var maximumGap = Math.Max(first.Height, second.Height) * 2.5f;
        var heightRatio = Math.Max(first.Height, second.Height) / Math.Max(1, Math.Min(first.Height, second.Height));
        return overlap / minimumWidth >= 0.25f && verticalGap <= maximumGap && heightRatio <= 2.5f;
    }

    private static PlateOcrFragment[] OrderForReading(PlateOcrFragment first, PlateOcrFragment second)
    {
        if (IsSameLine(first, second))
            return first.Left <= second.Left ? [first, second] : [second, first];
        return first.Top <= second.Top ? [first, second] : [second, first];
    }

    private static float HorizontalGap(PlateOcrFragment first, PlateOcrFragment second) =>
        Math.Max(0, Math.Max(first.Left, second.Left) - Math.Min(first.Right, second.Right));

    private static float VerticalGap(PlateOcrFragment first, PlateOcrFragment second) =>
        Math.Max(0, Math.Max(first.Top, second.Top) - Math.Min(first.Bottom, second.Bottom));

    private static char AsLetter(char value) => value switch
    {
        '0' => 'O', '1' => 'I', '2' => 'Z', '5' => 'S', '6' => 'G', '8' => 'B', _ => value,
    };

    private static char AsDigit(char value) => value switch
    {
        'O' or 'Q' or 'D' => '0', 'I' or 'L' => '1', 'Z' => '2', 'S' => '5', 'G' => '6', 'B' => '8', _ => value,
    };

    private sealed record PlateOcrFragment(
        IReadOnlyList<PlateOcrCharacter> Characters,
        float Left,
        float Top,
        float Right,
        float Bottom)
    {
        public float Width => Right - Left;
        public float Height => Bottom - Top;
    }
}

internal sealed record PlateOcrCharacter(char Value, float Confidence);

internal sealed record PlateOcrTextRegion(
    string Text,
    float Confidence,
    IReadOnlyList<PlateOcrCharacter> Characters,
    float Left,
    float Top,
    float Right,
    float Bottom);

internal sealed record PlateOcrCandidate(
    string PlateNumber,
    double Confidence,
    int ShapeScore,
    int NormalizationEdits,
    int SourceFragmentCount);
