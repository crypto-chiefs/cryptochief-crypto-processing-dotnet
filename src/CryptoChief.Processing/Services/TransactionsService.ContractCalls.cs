using System.Numerics;
using CryptoChief.Processing.Encoders.Evm;
using CryptoChief.Processing.Encoders.Solana;
using CryptoChief.Processing.Encoders.Ton;
using CryptoChief.Processing.Models;

namespace CryptoChief.Processing.Services;

public sealed partial class TransactionsService
{
    /// <summary>Sign an EVM contract call from a Solidity-style signature and args. Also handles TRON.</summary>
    public Task<SignTransactionResponse> SignEvmCallAsync(
        EvmCallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var dataHex = EvmAbi.EncodeCallHex(request.Method, request.Args.ToArray());
        var value = string.IsNullOrEmpty(request.Value) ? "0" : request.Value!;
        return SignAsync(new SignTransactionRequest
        {
            Network = request.Network,
            FromAddress = request.FromAddress,
            Type = TxType.Contract,
            UrlCallback = request.UrlCallback,
            Calls = new[]
            {
                new ContractCall { To = request.Contract, Value = value, Data = dataHex },
            },
        }, cancellationToken);
    }

    /// <summary>Alias of <see cref="SignEvmCallAsync"/> — TRON uses the same ABI encoding.</summary>
    public Task<SignTransactionResponse> SignTronCallAsync(
        EvmCallRequest request, CancellationToken cancellationToken = default) =>
        SignEvmCallAsync(request, cancellationToken);

    /// <summary>One-liner for an ERC-20 / TRC-20 <c>transfer(address,uint256)</c> call.</summary>
    public Task<SignTransactionResponse> Erc20TransferAsync(
        Erc20TransferRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SignEvmCallAsync(new EvmCallRequest
        {
            Network = request.Network,
            FromAddress = request.FromAddress,
            Contract = request.TokenContract,
            Method = "transfer(address,uint256)",
            Args = new object?[] { request.Recipient, request.Amount },
            UrlCallback = request.UrlCallback,
        }, cancellationToken);
    }

    /// <summary>Sign a Solana Anchor program call (8-byte discriminator + Borsh-encoded args).</summary>
    public Task<SignTransactionResponse> SignAnchorCallAsync(
        AnchorCallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var data = AnchorInstruction.Encode(request.Method, request.Args.ToArray());
        return SignAsync(new SignTransactionRequest
        {
            Network = request.Network,
            FromAddress = request.FromAddress,
            Type = TxType.Contract,
            UrlCallback = request.UrlCallback,
            Calls = new[]
            {
                new ContractCall
                {
                    To = request.Program,
                    Data = Convert.ToBase64String(data),
                    Accounts = request.Accounts,
                },
            },
        }, cancellationToken);
    }

    /// <summary>Sign a Solana program call with raw instruction bytes (non-Anchor programs).</summary>
    public Task<SignTransactionResponse> SignSolanaCallAsync(
        SolanaCallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SignAsync(new SignTransactionRequest
        {
            Network = request.Network,
            FromAddress = request.FromAddress,
            Type = TxType.Contract,
            UrlCallback = request.UrlCallback,
            Calls = new[]
            {
                new ContractCall
                {
                    To = request.Program,
                    Data = Convert.ToBase64String(request.InstructionData),
                    Accounts = request.Accounts,
                },
            },
        }, cancellationToken);
    }

    /// <summary>Sign a TON contract call with a pre-built BoC body cell.</summary>
    public Task<SignTransactionResponse> SignTonCallAsync(
        TonCallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var value = string.IsNullOrEmpty(request.Value) ? "0" : request.Value;
        return SignAsync(new SignTransactionRequest
        {
            Network = request.Network,
            FromAddress = request.FromAddress,
            Type = TxType.Contract,
            UrlCallback = request.UrlCallback,
            Calls = new[]
            {
                new ContractCall
                {
                    To = request.Contract,
                    Value = value,
                    Data = Convert.ToBase64String(request.BodyCell),
                    Bounce = request.Bounce,
                },
            },
        }, cancellationToken);
    }

    /// <summary>
    /// TON Jetton transfer. SDK builds the TEP-74 body, resolves the sender's
    /// Jetton wallet (if not pre-supplied), and picks a gas budget.
    /// </summary>
    public async Task<SignTransactionResponse> JettonTransferAsync(
        JettonTransferRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrEmpty(request.Recipient))
            throw new ArgumentException("JettonTransfer: Recipient required", nameof(request));
        if (string.IsNullOrEmpty(request.JettonMaster) && string.IsNullOrEmpty(request.JettonWalletAddress))
            throw new ArgumentException(
                "JettonTransfer: JettonMaster or JettonWalletAddress required", nameof(request));

        var rpc = Client.TonRpc;
        var jettonWallet = request.JettonWalletAddress;
        if (string.IsNullOrEmpty(jettonWallet))
            jettonWallet = await rpc.LookupJettonWalletAsync(
                request.JettonMaster!, request.FromAddress, cancellationToken).ConfigureAwait(false);

        var dest = TonAddress.Parse(request.Recipient);
        var respDest = TonAddress.Parse(
            string.IsNullOrEmpty(request.ResponseDestination)
                ? request.FromAddress
                : request.ResponseDestination!);

        TonCell? fwdPayload = null;
        if (!string.IsNullOrEmpty(request.Memo))
            fwdPayload = TonMessages.BuildTextCommentCell(request.Memo!);

        // 1 nanoTON forward delivers the transfer_notification (and memo); 0 skips it.
        var fwdInput = string.IsNullOrEmpty(request.ForwardTonAmount) && !string.IsNullOrEmpty(request.Memo)
            ? "1"
            : request.ForwardTonAmount ?? "0";
        var forwardTon = BigInteger.Parse(fwdInput, System.Globalization.CultureInfo.InvariantCulture);

        var body = TonMessages.BuildJettonTransferBody(
            request.QueryId, request.Amount, dest, respDest, null, forwardTon, fwdPayload);

        var attached = request.AttachedTon;
        if (string.IsNullOrEmpty(attached))
        {
            // 0.07 TON if recipient already has a Jetton wallet; 0.15 TON if it must be deployed.
            const string attachedNew      = "150000000";
            const string attachedExisting = "70000000";
            var hasWallet = await rpc.HasJettonWalletAsync(
                request.JettonMaster ?? "", request.Recipient, cancellationToken).ConfigureAwait(false);
            attached = hasWallet ? attachedExisting : attachedNew;
        }

        return await SignTonCallAsync(new TonCallRequest
        {
            Network = request.Network,
            FromAddress = request.FromAddress,
            Contract = jettonWallet!,
            BodyCell = body,
            Value = attached,
            Bounce = true,
            UrlCallback = request.UrlCallback,
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>TON NFT transfer (TEP-62 body).</summary>
    public Task<SignTransactionResponse> NftTransferAsync(
        NftTransferRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrEmpty(request.NftItem) || string.IsNullOrEmpty(request.NewOwner))
            throw new ArgumentException("NftTransfer: NftItem and NewOwner required", nameof(request));

        var newOwner = TonAddress.Parse(request.NewOwner);
        var respDest = TonAddress.Parse(
            string.IsNullOrEmpty(request.ResponseDestination)
                ? request.FromAddress
                : request.ResponseDestination!);
        var fwd = string.IsNullOrEmpty(request.ForwardTonAmount)
            ? BigInteger.Zero
            : BigInteger.Parse(request.ForwardTonAmount!, System.Globalization.CultureInfo.InvariantCulture);
        var body = TonMessages.BuildNftTransferBody(
            request.QueryId, newOwner, respDest, null, fwd, null);
        return SignTonCallAsync(new TonCallRequest
        {
            Network = request.Network,
            FromAddress = request.FromAddress,
            Contract = request.NftItem,
            BodyCell = body,
            Value = string.IsNullOrEmpty(request.AttachedTon) ? "50000000" : request.AttachedTon,
            Bounce = true,
            UrlCallback = request.UrlCallback,
        }, cancellationToken);
    }

    /// <summary>Send TON with a text comment (op=0 body). At least 1 nanoTON is required for the message to land.</summary>
    public Task<SignTransactionResponse> SendTonCommentAsync(
        TonCommentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrEmpty(request.Recipient))
            throw new ArgumentException("SendTonComment: Recipient required", nameof(request));
        var body = TonMessages.BuildTextCommentBody(request.Text ?? "");
        return SignTonCallAsync(new TonCallRequest
        {
            Network = request.Network,
            FromAddress = request.FromAddress,
            Contract = request.Recipient,
            BodyCell = body,
            Value = string.IsNullOrEmpty(request.AmountTon) ? "0" : request.AmountTon,
            Bounce = false,
            UrlCallback = request.UrlCallback,
        }, cancellationToken);
    }
}

public sealed record EvmCallRequest
{
    public required string Network { get; init; }
    public required string FromAddress { get; init; }
    public required string Contract { get; init; }

    /// <summary>Canonical Solidity signature, e.g. <c>transfer(address,uint256)</c>.</summary>
    public required string Method { get; init; }

    public required IReadOnlyList<object?> Args { get; init; }

    /// <summary>Native value (base units) attached to the call. Empty = 0.</summary>
    public string? Value { get; init; }

    public string? UrlCallback { get; init; }
}

public sealed record Erc20TransferRequest
{
    public required string Network { get; init; }
    public required string FromAddress { get; init; }
    public required string TokenContract { get; init; }
    public required string Recipient { get; init; }

    /// <summary>Token base units. Use <c>Amount.HumanToBase("12.5", 6)</c> for USDT-style amounts.</summary>
    public required BigInteger Amount { get; init; }

    public string? UrlCallback { get; init; }
}

public sealed record AnchorCallRequest
{
    public required string Network { get; init; }
    public required string FromAddress { get; init; }
    public required string Program { get; init; }
    public required string Method { get; init; }
    public required IReadOnlyList<BorshValue> Args { get; init; }

    /// <summary>Account metas in the order the program expects.</summary>
    public required IReadOnlyList<SolanaAccount> Accounts { get; init; }

    public string? UrlCallback { get; init; }
}

public sealed record SolanaCallRequest
{
    public required string Network { get; init; }
    public required string FromAddress { get; init; }
    public required string Program { get; init; }
    public required byte[] InstructionData { get; init; }
    public required IReadOnlyList<SolanaAccount> Accounts { get; init; }
    public string? UrlCallback { get; init; }
}

public sealed record TonCallRequest
{
    public required string Network { get; init; }
    public required string FromAddress { get; init; }
    public required string Contract { get; init; }
    public required byte[] BodyCell { get; init; }

    /// <summary>Attached TON in nanoTON decimal string. Empty = 0.</summary>
    public string? Value { get; init; }

    public bool? Bounce { get; init; }
    public string? UrlCallback { get; init; }
}

public sealed record JettonTransferRequest
{
    public required string Network { get; init; }
    public required string FromAddress { get; init; }

    /// <summary>Jetton master contract. Either this or <see cref="JettonWalletAddress"/> is required.</summary>
    public string? JettonMaster { get; init; }

    /// <summary>Sender's Jetton wallet. Empty → SDK resolves it.</summary>
    public string? JettonWalletAddress { get; init; }

    /// <summary>Recipient's MAIN TON wallet (NOT their Jetton wallet).</summary>
    public required string Recipient { get; init; }

    public required BigInteger Amount { get; init; }

    /// <summary>Where unused gas returns. Default: <see cref="FromAddress"/>.</summary>
    public string? ResponseDestination { get; init; }

    /// <summary>NanoTON budget. Empty → 0.07 TON for existing wallet, 0.15 TON for new.</summary>
    public string? AttachedTon { get; init; }

    /// <summary>Forwarded to the receiver's notification handler (nanoTON).</summary>
    public string? ForwardTonAmount { get; init; }

    /// <summary>Comment shown by wallets. Encoded as the canonical text-comment payload.</summary>
    public string? Memo { get; init; }

    public ulong QueryId { get; init; }
    public string? UrlCallback { get; init; }
}

public sealed record NftTransferRequest
{
    public required string Network { get; init; }
    public required string FromAddress { get; init; }
    public required string NftItem { get; init; }
    public required string NewOwner { get; init; }
    public string? ResponseDestination { get; init; }
    public string? AttachedTon { get; init; }
    public string? ForwardTonAmount { get; init; }
    public ulong QueryId { get; init; }
    public string? UrlCallback { get; init; }
}

public sealed record TonCommentRequest
{
    public required string Network { get; init; }
    public required string FromAddress { get; init; }
    public required string Recipient { get; init; }
    public string? Text { get; init; }

    /// <summary>NanoTON. At least 1 nanoTON is required for the message to reach the wallet.</summary>
    public string? AmountTon { get; init; }

    public string? UrlCallback { get; init; }
}
