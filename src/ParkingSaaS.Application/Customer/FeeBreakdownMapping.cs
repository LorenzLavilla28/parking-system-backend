using System.Text.Json;
using ParkingSaaS.Contracts.Customer;
using ParkingSaaS.Domain.Pricing;

namespace ParkingSaaS.Application.Customer;

internal static class FeeBreakdownMapping
{
    public static IReadOnlyList<FeeBreakdownItem> ToContract(this IReadOnlyList<PricingLineItem> items)
        => items.Select(i => new FeeBreakdownItem(i.Code, i.Description, i.Amount)).ToArray();

    public static string ToJson(this IReadOnlyList<PricingLineItem> items)
        => JsonSerializer.Serialize(items.Select(i => new FeeBreakdownItem(i.Code, i.Description, i.Amount)));

    public static IReadOnlyList<FeeBreakdownItem> FromJson(string json)
        => string.IsNullOrWhiteSpace(json)
            ? Array.Empty<FeeBreakdownItem>()
            : JsonSerializer.Deserialize<List<FeeBreakdownItem>>(json) ?? new List<FeeBreakdownItem>();
}
