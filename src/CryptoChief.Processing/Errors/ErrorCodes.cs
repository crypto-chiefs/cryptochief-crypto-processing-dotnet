namespace CryptoChief.Processing.Errors;

/// <summary>Stable error code strings used by the Crypto Chief API.</summary>
public static class ErrorCodes
{
    public const string InsufficientFunds        = "INSUFFICIENT_FUNDS";
    public const string InsufficientCredits      = "INSUFFICIENT_CREDITS";
    public const string DebtLimitExceeded        = "DEBT_LIMIT_EXCEEDED";
    public const string AssetNotEnabled          = "ASSET_NOT_ENABLED";
    public const string OrderAlreadyExists       = "ORDER_ALREADY_EXIST";
    public const string OrderCannotCancel        = "ORDER_CANNOT_CANCEL";
    public const string OrderNotLive             = "ORDER_NOT_LIVE";
    public const string AssetAlreadySelected     = "ASSET_ALREADY_SELECTED";
    public const string InvalidParams            = "INVALID_PARAMS";

    /// <summary>A wallet label over 255 characters — characters, not bytes.</summary>
    public const string LabelTooLong             = "LABEL_TOO_LONG";
    public const string ServiceError             = "SERVICE_ERROR";
    public const string Unauthorized             = "UNAUTHORIZED";
    public const string UrlCallbackRequired      = "URL_CALLBACK_REQUIRED";
    public const string BatchEmpty               = "BATCH_EMPTY";
    public const string BatchTooLarge            = "BATCH_TOO_LARGE";
    public const string BatchDuplicateOrderId    = "BATCH_DUPLICATE_ORDER_ID";
    public const string FromWalletNotOwned       = "FROM_WALLET_NOT_OWNED";
    public const string SignatureExpired         = "SIGNATURE_EXPIRED";
    public const string AlreadyExecuted          = "ALREADY_EXECUTED";
    public const string PreflightFailed          = "PREFLIGHT_FAILED";
    public const string BroadcastFailed          = "BROADCAST_FAILED";
    public const string SignedTxMismatch         = "SIGNED_TX_MISMATCH";
    public const string ContractRequired         = "CONTRACT_REQUIRED_FOR_TOKEN";
    public const string TransferFieldsForbid     = "TRANSFER_FIELDS_NOT_ALLOWED_FOR_CONTRACT";
    public const string CallsRequired            = "CALLS_REQUIRED";
    public const string CallsNotAllowed          = "CALLS_NOT_ALLOWED_FOR_TRANSFER";
    public const string ContractCallsUnsupported = "CONTRACT_CALLS_UNSUPPORTED_ON_NETWORK";
    public const string NetworkError             = "NETWORK_ERROR";

    /// <summary>The object does not exist OR is not this project's — deliberately indistinguishable.</summary>
    public const string NotFound                 = "NOT_FOUND";
    /// <summary>Webhook resend: a newer event exists for the same object; only the latest may be resent. Permanent.</summary>
    public const string DeliverySuperseded       = "DELIVERY_SUPERSEDED";
    /// <summary>Webhook resend: a worker holds the delivery, or it is already scheduled for a retry.</summary>
    public const string DeliveryInFlight         = "DELIVERY_IN_FLIGHT";
    /// <summary>Webhook resend: resent under a minute ago (HTTP 429, Retry-After).</summary>
    public const string ResendTooSoon            = "RESEND_TOO_SOON";
    /// <summary>Static-deposit resend: no webhook was ever queued — the wallet had no callback_url.</summary>
    public const string NoDeliveries             = "NO_DELIVERIES";
}
