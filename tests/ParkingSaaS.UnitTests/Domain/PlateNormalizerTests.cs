using FluentAssertions;
using ParkingSaaS.Domain.Services;
using Xunit;

namespace ParkingSaaS.UnitTests.Domain;

public sealed class PlateNormalizerTests
{
    private readonly PlateNormalizer _normalizer = new();

    [Theory]
    [InlineData("ABC 1234", "ABC1234")]
    [InlineData("abc-1234", "ABC1234")]
    [InlineData("  abc 1234  ", "ABC1234")]
    [InlineData("ABC.1234", "ABC1234")]
    [InlineData("a b c 1 2 3 4", "ABC1234")]
    [InlineData("ABC_1234", "ABC1234")]
    public void Normalizes_to_canonical_form(string input, string expected)
        => _normalizer.Normalize(input).Should().Be(expected);

    [Fact]
    public void Different_formattings_of_same_plate_match()
    {
        _normalizer.Normalize("ABC 1234").Should().Be(_normalizer.Normalize("abc-1234"));
    }

    [Fact]
    public void Genuinely_different_plates_do_not_match()
    {
        _normalizer.Normalize("ABC1234").Should().NotBe(_normalizer.Normalize("ABC1235"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("---")]
    public void Empty_or_punctuation_only_yields_empty(string input)
        => _normalizer.Normalize(input).Should().BeEmpty();

    [Fact]
    public void Non_ascii_characters_are_stripped()
        => _normalizer.Normalize("ABÇ café 12").Should().Be("ABCAF12");
}
