namespace CryptoChief.Processing.Models;

/// <summary>
/// One coin or token on one network. The same shape on both asset endpoints: what the
/// project can be paid in right now (<c>BlockchainService.ContractsAvailableAsync</c>) and
/// everything the platform supports (<c>BlockchainService.ContractsListAsync</c>).
/// </summary>
public sealed record AvailableContract
{
    public string Network { get; init; } = string.Empty;
    public string Coin { get; init; } = string.Empty;

    /// <summary>
    /// The token contract, or an empty string for a native coin. Empty is the answer — the
    /// API sends <c>""</c> rather than null, and <see cref="Type"/> says <c>native</c>.
    /// </summary>
    public string? Contract { get; init; }

    /// <summary>Either <c>native</c> or <c>token</c>.</summary>
    public string? Type { get; init; }

    /// <summary>
    /// The protocol family the asset belongs to — one of the
    /// <see cref="Chains.ChainFamily"/> values, upper-case.
    /// </summary>
    public string ChainFamily { get; init; } = string.Empty;

    /// <summary>
    /// True when the asset lives on a test network. Worth reading on the platform
    /// catalogue, where mainnet and testnet assets arrive in one list.
    /// </summary>
    public bool IsTest { get; init; }

    public int Decimals { get; init; }
}

public sealed record AvailableContractsResponse
{
    public IReadOnlyList<AvailableContract> Items { get; init; } = Array.Empty<AvailableContract>();
}

/// <summary>
/// A chain the platform's blockchain scanner is currently connected to.
/// </summary>
/// <remarks>
/// Infrastructure-level information: which chains the platform can read blocks from right
/// now. It is not the project's asset catalogue — for what the project can actually be paid
/// in, use <c>BlockchainService.ContractsAvailableAsync</c>.
/// </remarks>
public sealed record SupportedBlockchain
{
    /// <summary>The chain key — one of the <see cref="Chains.Chain"/> values.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// The protocol family the scanner reads the chain with, lower-case (<c>evm</c>,
    /// <c>tron</c>, <c>solana</c>…). Not the upper-case
    /// <see cref="Chains.ChainFamily"/> that assets and wallets are labelled with.
    /// </summary>
    public string Type { get; init; } = string.Empty;
}

public sealed record WalletBalanceRow
{
    public string? Contract { get; init; }
    public string Address { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string HumanValue { get; init; } = string.Empty;
    public int Decimals { get; init; }
}

public sealed record TxStatusRow
{
    public int Confirmations { get; init; }
    public string? Fee { get; init; }
    public string? HumanFee { get; init; }
    public long? BlockNumber { get; init; }
    public string? Status { get; init; }
}
