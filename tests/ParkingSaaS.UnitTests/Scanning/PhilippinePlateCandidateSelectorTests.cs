using FluentAssertions;
using ParkingSaaS.Infrastructure.Scanning;

namespace ParkingSaaS.UnitTests.Scanning;

public sealed class PhilippinePlateCandidateSelectorTests
{
    [Fact]
    public void Merges_adjacent_mixed_format_fragments()
    {
        var result = Select(
            Region("C4", 100, 40, 155, 80),
            Region("J758", 170, 40, 280, 80));

        result!.PlateNumber.Should().Be("C4J758");
    }

    [Fact]
    public void Supports_number_first_motorcycle_or_temporary_formats()
    {
        var result = Select(
            Region("956", 80, 40, 180, 95),
            Region("QOT", 195, 40, 300, 95));

        result!.PlateNumber.Should().Be("956QOT");
        result.ShapeScore.Should().Be(6);
    }

    [Fact]
    public void Supports_stacked_motorcycle_plate_text()
    {
        var result = Select(
            Region("ABC", 100, 20, 220, 65),
            Region("1234", 105, 75, 225, 120));

        result!.PlateNumber.Should().Be("ABC1234");
    }

    [Theory]
    [InlineData("AHA 8208", "AHA8208")]
    [InlineData("WTC-259", "WTC259")]
    [InlineData("GA01176", "GA01176")]
    [InlineData("A123VA", "A123VA")]
    [InlineData("C0B553", "C0B553")]
    [InlineData("12345", "12345")]
    public void Accepts_diverse_philippine_plate_series(string text, string expected)
    {
        Select(Region(text, 10, 10, 250, 80))!.PlateNumber.Should().Be(expected);
    }

    [Fact]
    public void Removes_a_weak_interior_separator_without_dropping_plate_letters()
    {
        var result = Select(Region(
            "PAQ1323",
            10,
            10,
            250,
            80,
            characterConfidence: new Dictionary<int, float> { [2] = 0.60f, [3] = 0.61f }));

        result!.PlateNumber.Should().Be("PAQ323");
    }

    [Fact]
    public void Rejects_headers_years_and_registration_serials()
    {
        var result = Select(
            Region("REGISTERED", 10, 5, 200, 30),
            Region("2007", 300, 40, 350, 80),
            Region("3370173", 300, 85, 390, 105),
            Region("MV FILE NO. 0701-00001058818", 10, 120, 400, 145));

        result.Should().BeNull();
    }

    private static PlateOcrCandidate? Select(params PlateOcrTextRegion[] regions) =>
        PhilippinePlateCandidateSelector.SelectBest(regions, minimumConfidence: 0.80);

    private static PlateOcrTextRegion Region(
        string text,
        float left,
        float top,
        float right,
        float bottom,
        float confidence = 0.99f,
        IReadOnlyDictionary<int, float>? characterConfidence = null)
    {
        var index = 0;
        var characters = text
            .ToUpperInvariant()
            .Where(char.IsAsciiLetterOrDigit)
            .Select(value =>
            {
                var score = characterConfidence?.GetValueOrDefault(index, confidence) ?? confidence;
                index++;
                return new PlateOcrCharacter(value, score);
            })
            .ToArray();
        return new PlateOcrTextRegion(text, confidence, characters, left, top, right, bottom);
    }
}
