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
    /// <summary>Package version, as NuGet publishes it.</summary>
    public static readonly string Version = ReadVersion();

    private static string ReadVersion()
    {
        var informational = typeof(CryptoChiefClient).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(informational))
        {
            // Source Link appends "+<commit>" build metadata; it is not part of the version.
            var plus = informational!.IndexOf('+');
            return plus < 0 ? informational : informational[..plus];
        }

        var assembly = typeof(CryptoChiefClient).Assembly.GetName().Version;
        if (assembly is null) return "0.0.0";
        return $"{assembly.Major}.{assembly.Minor}.{Math.Max(assembly.Build, 0)}";
    }

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

    private CryptoChiefClient(CryptoChiefClient source, CryptoChiefHttpTransport transport)
    {
        Options = source.Options;
        HttpClient = source.HttpClient;
        Logger = source.Logger;
        Transport = transport;
        _tonRpc = source._tonRpc;

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

    /// <summary>
    /// A client over the same HTTP connection pool and clock offset that sends
    /// <c>Idempotency-Key</c> on every call it makes:
    /// <code>
    /// await client.WithIdempotencyKey("payout-2026-09-16-0001").Payouts.ExecuteAsync(request);
    /// </code>
    /// </summary>
    /// <remarks>
    /// The header is part of the string to sign, so it has to be set before the client signs:
    /// one added by a <see cref="DelegatingHandler"/> is not covered by the signature and the
    /// server answers 401 <c>INVALID_SIGNATURE</c>.
    /// <para>The platform keeps the value in the billing record of the call, up to 255 bytes. It
    /// does not deduplicate payouts — <c>ExecutePayoutRequest.OrderId</c> does that.</para>
    /// </remarks>
    /// <param name="key">Printable ASCII with no space or tab at either edge — the server trims
    /// those before it verifies the signature, so an untrimmed value would be signed in a form it
    /// never sees. An empty key returns a client that sends no header.</param>
    /// <exception cref="ArgumentException">The key cannot be sent as it is.</exception>
    public CryptoChiefClient WithIdempotencyKey(string? key) =>
        new(this, Transport.WithIdempotencyKey(key));

    /// <summary>
    /// Signed request to <paramref name="path"/>, decoded into <typeparamref name="T"/> — the
    /// low-level entry point behind every service method, for a route this SDK has no method for.
    /// Same signing, retries, clock correction and error envelope.
    /// <code>
    /// var balance = await client.RequestAsync&lt;JsonElement&gt;(
    ///     HttpMethod.Get, "/v1/balance?coin=TRX");
    /// </code>
    /// </summary>
    /// <remarks>
    /// The method is signed and sent upper-cased over <c>a</c>–<c>z</c>; <c>GET</c> and
    /// <c>HEAD</c> take no body. <paramref name="path"/> starts with <c>/</c> and holds the route
    /// without the base URL; a query goes on it as <c>?a=1&amp;b=2</c> and is signed as the URL
    /// carries it, while the path itself is signed percent-decoded — the form the server reads.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="path"/> does not start with <c>/</c>,
    /// is not valid percent-encoding, or a body was given to a method that takes none.</exception>
    public Task<T> RequestAsync<T>(
        HttpMethod method, string path, object? body = null, CancellationToken cancellationToken = default) =>
        Transport.SendAsync<T>(method, path, body, cancellationToken);

    /// <summary>Signed <c>POST</c> to <paramref name="path"/> — <see cref="RequestAsync{T}(HttpMethod, string, object, CancellationToken)"/>
    /// with <see cref="HttpMethod.Post"/>.</summary>
    /// <inheritdoc cref="RequestAsync{T}(HttpMethod, string, object, CancellationToken)" path="/exception"/>
    public Task<T> RequestAsync<T>(
        string path, object? body = null, CancellationToken cancellationToken = default) =>
        Transport.SendAsync<T>(HttpMethod.Post, path, body, cancellationToken);

    internal TonRpcClient TonRpc => _tonRpc ??= new TonRpcClient(
        Options.MerchantId, Options.TonRpcBaseUrl, HttpClient, Options.UserAgent);

    private static HttpClient NewDefaultHttpClient(CryptoChiefClientOptions options) =>
        new() { Timeout = options.Timeout };
}
