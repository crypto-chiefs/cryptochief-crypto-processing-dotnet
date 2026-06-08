using System.Numerics;

namespace CryptoChief.Processing.Encoders.Ton;

/// <summary>Standard message-body builders for Jetton (TEP-74), NFT (TEP-62), and text-comment payloads.</summary>
public static class TonMessages
{
    public const uint OpJettonTransfer = 0x0F8A7EA5;
    public const uint OpNftTransfer    = 0x5FCC3D14;
    public const uint OpTextComment    = 0x00000000;

    public static byte[] BuildJettonTransferBody(
        ulong queryId,
        BigInteger amount,
        TonAddress destination,
        TonAddress? responseDestination,
        TonCell? customPayload,
        BigInteger forwardTon,
        TonCell? forwardPayload)
    {
        if (amount.Sign < 0) throw new ArgumentOutOfRangeException(nameof(amount), "negative");
        ArgumentNullException.ThrowIfNull(destination);

        var body = new TonCell()
            .StoreUInt(OpJettonTransfer, 32)
            .StoreUInt(queryId, 64)
            .StoreCoins(amount)
            .StoreAddress(destination)
            .StoreAddress(responseDestination)
            .StoreMaybeRef(customPayload)
            .StoreCoins(forwardTon);

        if (forwardPayload is not null)
            body.StoreBit(true).StoreRef(forwardPayload);
        else
            body.StoreBit(false);

        return body.ToBoc();
    }

    public static byte[] BuildNftTransferBody(
        ulong queryId,
        TonAddress newOwner,
        TonAddress? responseDestination,
        TonCell? customPayload,
        BigInteger forwardTon,
        TonCell? forwardPayload)
    {
        ArgumentNullException.ThrowIfNull(newOwner);

        var body = new TonCell()
            .StoreUInt(OpNftTransfer, 32)
            .StoreUInt(queryId, 64)
            .StoreAddress(newOwner)
            .StoreAddress(responseDestination)
            .StoreMaybeRef(customPayload)
            .StoreCoins(forwardTon);

        if (forwardPayload is not null)
            body.StoreBit(true).StoreRef(forwardPayload);
        else
            body.StoreBit(false);

        return body.ToBoc();
    }

    public static TonCell BuildTextCommentCell(string text) =>
        new TonCell().StoreUInt(OpTextComment, 32).StoreStringSnake(text);

    public static byte[] BuildTextCommentBody(string text) =>
        BuildTextCommentCell(text).ToBoc();
}
