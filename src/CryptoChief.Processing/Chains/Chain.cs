namespace CryptoChief.Processing.Chains;

/// <summary>Chain codes (the value of <c>network</c> / <c>chain</c> / <c>network_code</c> fields).</summary>
public static class Chain
{
    public const string EthMainnet      = "ETH_MAINNET";
    public const string EthSepolia      = "ETH_SEPOLIA";
    public const string BscMainnet      = "BSC_MAINNET";
    public const string BscTestnet      = "BSC_TESTNET";
    public const string PolygonMainnet  = "POLYGON_MAINNET";
    public const string PolygonAmoy     = "POLYGON_AMOY";
    public const string ArbitrumOne     = "ARBITRUM_ONE";
    public const string ArbitrumSepolia = "ARBITRUM_SEPOLIA";
    public const string OptimismMainnet = "OPTIMISM_MAINNET";
    public const string OptimismSepolia = "OPTIMISM_SEPOLIA";
    public const string AvaxMainnet     = "AVAX_MAINNET";
    public const string AvaxTestnet     = "AVAX_TESTNET";

    public const string BtcMainnet      = "BTC_MAINNET";
    public const string BtcTestnet      = "BTC_TESTNET_4";
    public const string LitecoinMainnet = "LITECOIN_MAINNET";
    public const string BitcoinCash     = "BITCOIN_CASH_MAINNET";
    public const string Dogecoin        = "DOGECOIN_MAINNET";

    public const string TronMainnet = "TRON_MAINNET";
    public const string TronNile    = "TRON_NILE";

    public const string SolanaMainnet = "SOLANA_MAINNET";
    public const string SolanaDevnet  = "SOLANA_DEVNET";

    public const string TonMainnet = "TON_MAINNET";
    public const string TonTestnet = "TON_TESTNET";

    public const string XrpMainnet = "XRP_MAINNET";
    public const string XrpTestnet = "XRP_TESTNET";

    private static readonly Dictionary<string, string> ChainToFamily = new(StringComparer.OrdinalIgnoreCase)
    {
        [EthMainnet]      = ChainFamily.Evm,
        [EthSepolia]      = ChainFamily.Evm,
        [BscMainnet]      = ChainFamily.Evm,
        [BscTestnet]      = ChainFamily.Evm,
        [PolygonMainnet]  = ChainFamily.Evm,
        [PolygonAmoy]     = ChainFamily.Evm,
        [ArbitrumOne]     = ChainFamily.Evm,
        [ArbitrumSepolia] = ChainFamily.Evm,
        [OptimismMainnet] = ChainFamily.Evm,
        [OptimismSepolia] = ChainFamily.Evm,
        [AvaxMainnet]     = ChainFamily.Evm,
        [AvaxTestnet]     = ChainFamily.Evm,
        [BtcMainnet]      = ChainFamily.BtcUtxo,
        [BtcTestnet]      = ChainFamily.BtcUtxoTestnet,
        [LitecoinMainnet] = ChainFamily.LitecoinUtxo,
        [BitcoinCash]     = ChainFamily.BitcoinCashUtxo,
        [Dogecoin]        = ChainFamily.DogecoinUtxo,
        [TronMainnet]     = ChainFamily.Tron,
        [TronNile]        = ChainFamily.Tron,
        [SolanaMainnet]   = ChainFamily.Solana,
        [SolanaDevnet]    = ChainFamily.Solana,
        [TonMainnet]      = ChainFamily.Ton,
        [TonTestnet]      = ChainFamily.Ton,
        [XrpMainnet]      = ChainFamily.XrpLedger,
        [XrpTestnet]      = ChainFamily.XrpLedger,
    };

    public static string? GetFamily(string chain) =>
        ChainToFamily.TryGetValue(chain, out var f) ? f : null;
}

/// <summary>Chain-family codes (the value of <c>chain_family</c> in API responses).</summary>
public static class ChainFamily
{
    public const string Evm              = "EVM";
    public const string Tron             = "TRON";
    public const string Solana           = "SOLANA";
    public const string XrpLedger        = "XRP_LEDGER";
    public const string Ton              = "TON";
    public const string BtcUtxo          = "BTC_UTXO";
    public const string BtcUtxoTestnet   = "BTC_UTXO_TESTNET";
    public const string DogecoinUtxo     = "DOGECOIN_UTXO";
    public const string BitcoinCashUtxo  = "BTC_CASH_UTXO";
    public const string LitecoinUtxo     = "LITECOIN_UTXO";

    /// <summary>True for EVM / TRON / Solana / TON.</summary>
    public static bool SupportsContractCalls(string family) => family switch
    {
        Evm or Tron or Solana or Ton => true,
        _ => false,
    };
}
