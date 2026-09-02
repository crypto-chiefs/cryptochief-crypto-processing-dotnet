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

/// <summary>
/// A fiat currency the platform can price an order in — the <c>currency</c> of a fiat-mode
/// pay-in and the <c>from</c>/<c>to</c> of a rate quote.
/// </summary>
public sealed record FiatCurrency
{
    /// <summary>ISO 4217 code, e.g. <c>SEK</c>.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Display name, e.g. <c>Swedish Krona</c>.</summary>
    public string Name { get; init; } = string.Empty;
}

/// <summary>
/// The crypto tickers the platform has a rate for, against <see cref="Quote"/>.
/// </summary>
/// <remarks>
/// Rate availability only: a ticker here can be quoted, which does not mean the platform
/// takes deposits, sweeps or payouts in it. For that, read
/// <c>BlockchainService.ContractsAvailableAsync</c>.
/// </remarks>
public sealed record CryptoCurrencies
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NoExchanges =
        new Dictionary<string, IReadOnlyList<string>>();

    private readonly IReadOnlyList<string> _tickers = Array.Empty<string>();
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _byExchange = NoExchanges;

    /// <summary>
    /// Every ticker, deduplicated across the exchanges. Empty rather than null when the
    /// platform has none — the API spells an empty list <c>null</c> on the wire.
    /// </summary>
    public IReadOnlyList<string> Tickers
    {
        get => _tickers;
        init => _tickers = value is null ? Array.Empty<string>() : value;
    }

    /// <summary>
    /// The tickers each exchange carries, keyed by exchange name. Neither the map nor any
    /// exchange's list is ever null: an exchange the platform currently carries nothing
    /// from arrives as <c>null</c> and reaches you as an empty list.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ByExchange
    {
        get => _byExchange;
        init => _byExchange = NormaliseExchanges(value);
    }

    /// <summary>How many tickers <see cref="Tickers"/> holds.</summary>
    public int Count { get; init; }

    /// <summary>The asset the rates are quoted against — <c>USDT</c>.</summary>
    public string Quote { get; init; } = string.Empty;

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> NormaliseExchanges(
        IReadOnlyDictionary<string, IReadOnlyList<string>>? value)
    {
        if (value is null) return NoExchanges;

        var carriesNull = false;
        foreach (var pair in value)
        {
            if (pair.Value is null) { carriesNull = true; break; }
        }
        if (!carriesNull) return value;

        var copy = new Dictionary<string, IReadOnlyList<string>>(value.Count);
        foreach (var pair in value)
            copy[pair.Key] = pair.Value is null ? Array.Empty<string>() : pair.Value;
        return copy;
    }
}
