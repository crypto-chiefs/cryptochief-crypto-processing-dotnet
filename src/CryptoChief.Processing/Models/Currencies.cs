namespace CryptoChief.Processing.Models;

public sealed record ConvertRequest
{
    public string? Provider { get; init; }
    public required string From { get; init; }
    public required string To { get; init; }
    public required string Amount { get; init; }
}

public sealed record ConvertResponse
{
    public double AmountCrypto { get; init; }
    public double AmountFiat { get; init; }
    public string? Crypto { get; init; }
    public double CryptoToUsdt { get; init; }
    public string? Exchange { get; init; }
    public string? Fiat { get; init; }
    public double FiatToUsd { get; init; }
    public long TimestampCrypto { get; init; }
    public long TimestampFiat { get; init; }
}
