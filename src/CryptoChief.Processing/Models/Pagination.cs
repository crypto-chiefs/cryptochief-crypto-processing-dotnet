namespace CryptoChief.Processing.Models;

public sealed record HistoryQuery
{
    public int? Page { get; init; }
    public int? PageSize { get; init; }
    public string? Status { get; init; }
    public string? Coin { get; init; }
    public string? Network { get; init; }
    public string? DateFrom { get; init; }
    public string? DateTo { get; init; }
}

public sealed record HistoryMeta
{
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int Total { get; init; }
    public int? TotalPages { get; init; }
}
