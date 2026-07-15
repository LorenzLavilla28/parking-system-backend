namespace ParkingSaaS.Domain.Services;

/// <summary>
/// Masks a plate for public display so a customer can confirm they have the
/// right vehicle without the page revealing the full plate to a bystander.
/// Shows the first and last two characters of the raw plate, masking the middle.
/// </summary>
public static class PlateMasker
{
    public static string Mask(string rawPlate)
    {
        if (string.IsNullOrWhiteSpace(rawPlate))
            return string.Empty;

        var plate = rawPlate.Trim();
        if (plate.Length <= 4)
            return new string('•', plate.Length);

        var head = plate[..2];
        var tail = plate[^2..];
        var maskedMiddle = new string('•', plate.Length - 4);
        return $"{head}{maskedMiddle}{tail}";
    }
}
