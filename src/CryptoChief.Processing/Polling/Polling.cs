using CryptoChief.Processing.Errors;
using CryptoChief.Processing.Models;

namespace CryptoChief.Processing.Polling;

public sealed record PollOptions
{
    /// <summary>Default timeout of <see cref="PollingExtensions.WaitForPayoutAsync"/> without options: 90 minutes.</summary>
    public static readonly TimeSpan PayoutTimeout = TimeSpan.FromMinutes(90);

    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Default 10 minutes, for payouts too; for a payout set <c>Timeout = PayoutTimeout</c>.
    /// <see cref="PollingExtensions.WaitForPayoutAsync"/> without options waits
    /// <see cref="PayoutTimeout"/>. On expiry a <see cref="TimeoutException"/> is thrown: the object is
    /// not terminal yet, not failed; its last state is in <c>Data["LastSnapshot"]</c>.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);
}

/// <summary>Block until a payout / transaction / pay-in reaches a terminal state.</summary>
public static class PollingExtensions
{
    /// <summary>
    /// Polls until the payout is terminal. It stays <see cref="PayoutStatus.ConfirmCheck"/> until
    /// every source reaches <see cref="PayoutInfo.RequiredConfirmations"/>. Without
    /// <paramref name="options"/> it waits <see cref="PollOptions.PayoutTimeout"/>. A
    /// <see cref="TimeoutException"/> means the payout is not finished yet, not that it failed.
    /// </summary>
    public static Task<PayoutInfo> WaitForPayoutAsync(
        this CryptoChiefClient client, string uuid,
        PollOptions? options = null, CancellationToken cancellationToken = default) =>
        PollUntilTerminalAsync(PayoutOptions(options),
            ct => client.Payouts.InfoAsync(uuid, ct),
            p => p.IsTerminal,
            cancellationToken);

    internal static PollOptions PayoutOptions(PollOptions? options) =>
        options ?? new PollOptions { Timeout = PollOptions.PayoutTimeout };

    public static Task<TransactionInfo> WaitForTransactionAsync(
        this CryptoChiefClient client, string uuid,
        PollOptions? options = null, CancellationToken cancellationToken = default) =>
        PollUntilTerminalAsync(options,
            ct => client.Transactions.InfoAsync(uuid, ct),
            t => t.IsTerminal,
            cancellationToken);

    public static Task<PayIn> WaitForPayInAsync(
        this CryptoChiefClient client, string uuid,
        PollOptions? options = null, CancellationToken cancellationToken = default) =>
        PollUntilTerminalAsync(options,
            ct => client.PayIns.InfoAsync(uuid, ct),
            p => p.IsTerminal,
            cancellationToken);

    private static async Task<T> PollUntilTerminalAsync<T>(
        PollOptions? options,
        Func<CancellationToken, Task<T>> fetch,
        Func<T, bool> terminal,
        CancellationToken cancellationToken) where T : class
    {
        options ??= new PollOptions();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(options.Timeout);

        T? last = null;
        while (true)
        {
            try
            {
                last = await fetch(cts.Token).ConfigureAwait(false);
                if (terminal(last)) return last;
            }
            catch (CryptoChiefApiException ex) when (ex.IsRetryable)
            {
            }
            catch (OperationCanceledException) when (IsTimeout(cts, cancellationToken))
            {
                throw TimeoutSnapshot(options.Timeout, last);
            }

            try
            {
                await Task.Delay(options.Interval, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (IsTimeout(cts, cancellationToken))
            {
                throw TimeoutSnapshot(options.Timeout, last);
            }
        }
    }

    private static bool IsTimeout(CancellationTokenSource cts, CancellationToken userToken) =>
        cts.IsCancellationRequested && !userToken.IsCancellationRequested;

    private static TimeoutException TimeoutSnapshot<T>(TimeSpan timeout, T? last) where T : class
    {
        var ex = new TimeoutException(
            $"cryptochief: poll did not reach terminal in {timeout}.");
        if (last is not null) ex.Data["LastSnapshot"] = last;
        return ex;
    }
}
