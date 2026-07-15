using FluentAssertions;
using ParkingSaaS.Domain.Services;
using Xunit;

namespace ParkingSaaS.UnitTests.Domain;

public sealed class PlateMaskerTests
{
    [Fact]
    public void Masks_the_middle_keeping_first_and_last_two()
        => PlateMasker.Mask("ABC1234").Should().Be("AB•••34");

    [Fact]
    public void Short_plates_are_fully_masked()
        => PlateMasker.Mask("AB12").Should().Be("••••");

    [Fact]
    public void Empty_input_returns_empty()
        => PlateMasker.Mask("").Should().BeEmpty();

    [Fact]
    public void Masked_output_does_not_contain_the_full_plate()
    {
        var masked = PlateMasker.Mask("XYZ9876");
        masked.Should().NotBe("XYZ9876");
        masked.Should().Contain("•");
    }
}
