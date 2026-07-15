namespace ParkingSaaS.Contracts.Common;

/// <summary>Standard success envelope: <c>{ data, meta }</c>.</summary>
public sealed record ApiResponse<T>(T Data, object? Meta = null)
{
    public static ApiResponse<T> Ok(T data, object? meta = null) => new(data, meta);
}

/// <summary>Pagination request parameters shared by list endpoints.</summary>
public sealed record PageQuery
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string? Sort { get; init; }
    public string? Search { get; init; }

    public int NormalizedPage => Page < 1 ? 1 : Page;
    public int NormalizedPageSize => PageSize is < 1 or > 200 ? 20 : PageSize;
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
