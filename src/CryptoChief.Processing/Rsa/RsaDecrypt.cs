using System.Security.Cryptography;
using System.Text;

namespace CryptoChief.Processing.Rsa;

/// <summary>RSA-OAEP/SHA-256 — the algorithm the API uses for <c>private_key_encrypted</c>.</summary>
public static class RsaDecrypt
{
    /// <summary>Decrypt a base64 RSA-OAEP/SHA-256 ciphertext. Returns UTF-8 plaintext.</summary>
    public static string DecryptOaepSha256(RSA privateKey, string base64Ciphertext)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        ArgumentNullException.ThrowIfNull(base64Ciphertext);

        byte[] ct;
        try { ct = Convert.FromBase64String(base64Ciphertext); }
        catch (FormatException ex)
        {
            throw new CryptographicException(
                $"cryptochief: RSA decrypt: bad base64: {ex.Message}", ex);
        }
        var pt = privateKey.Decrypt(ct, RSAEncryptionPadding.OaepSHA256);
        return Encoding.UTF8.GetString(pt);
    }
}
