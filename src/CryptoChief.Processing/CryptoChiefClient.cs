using System.Reflection;
using CryptoChief.Processing.Http;
using CryptoChief.Processing.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CryptoChief.Processing;

/// <summary>Entry point to the Crypto Chief processing API. Safe for concurrent use; reuse one instance across the app.</summary>
public sealed class CryptoChiefClient
{
    public static readonly string Version =
        typeof(CryptoChiefClient).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(CryptoChiefClient).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    internal CryptoChiefClientOptions Options { get; }
    internal CryptoChiefHttpTransport Transport { get; }
    internal ILogger Logger { get; }
    internal HttpClient HttpClient { get; }

    private TonRpcClient? _tonRpc;

    public PayoutsService Payouts { get; }
    public TransactionsService Transactions { get; }
    public PayInsService PayIns { get; }
    public WalletsService Wallets { get; }
    public SweepsService Sweeps { get; }
    public WithdrawalsService Withdrawals { get; }
    public StaticDepositsService StaticDeposits { get; }
    public BlockchainService Blockchain { get; }
    public CurrenciesService Currencies { get; }
    public CreditsService Credits { get; }
    public WebhooksService Webhooks { get; }

    public CryptoChiefClient(string merchantId, string apiKey)
        : this(new CryptoChiefClientOptions { MerchantId = merchantId, ApiKey = apiKey }) { }

    public CryptoChiefClient(CryptoChiefClientOptions options)
        : this(options, NewDefaultHttpClient(options), NullLogger<CryptoChiefClient>.Instance) { }

    public CryptoChiefClient(
        IOptions<CryptoChiefClientOptions> options,
        HttpClient httpClient,
        ILogger<CryptoChiefClient>? logger = null)
        : this(options.Value, httpClient, logger ?? NullLogger<CryptoChiefClient>.Instance) { }

    public CryptoChiefClient(
        CryptoChiefClientOptions options,
        HttpClient httpClient,
        ILogger<CryptoChiefClient>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClient);
        options.Validate();

        Options = options;
        HttpClient = httpClient;
        Logger = logger ?? NullLogger<CryptoChiefClient>.Instance;
        Transport = new CryptoChiefHttpTransport(httpClient, options, Logger);

        Payouts        = new PayoutsService(this);
        Transactions   = new TransactionsService(this);
        PayIns         = new PayInsService(this);
        Wallets        = new WalletsService(this);
        Sweeps         = new SweepsService(this);
        Withdrawals    = new WithdrawalsService(this);
        StaticDeposits = new StaticDepositsService(this);
        Blockchain     = new BlockchainService(this);
        Currencies     = new CurrenciesService(this);
        Credits        = new CreditsService(this);
        Webhooks       = new WebhooksService(this);
    }

    public string MerchantId => Options.MerchantId;
    public string BaseUrl => Options.BaseUrl;

    internal TonRpcClient TonRpc => _tonRpc ??= new TonRpcClient(
        Options.MerchantId, Options.TonRpcBaseUrl, HttpClient, Options.UserAgent);

    private static HttpClient NewDefaultHttpClient(CryptoChiefClientOptions options) =>
        new() { Timeout = options.Timeout };
}
