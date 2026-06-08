using System.Security.Cryptography;
using System.Text;
using CryptoChief.Processing.Rsa;
using FluentAssertions;
using Xunit;

namespace CryptoChief.Processing.Tests;

public class RsaTests
{
    [Fact]
    public void RoundTrip_encrypts_then_decrypts_via_loaded_key()
    {
        using var rsa = RSA.Create(2048);
        var pkcs8 = rsa.ExportPkcs8PrivateKey();
        var pem = "-----BEGIN PRIVATE KEY-----\n"
                + Convert.ToBase64String(pkcs8).Chunked(64)
                + "\n-----END PRIVATE KEY-----";

        const string secret = "abcd1234-private-key-hex";
        var cipher = rsa.Encrypt(Encoding.UTF8.GetBytes(secret), RSAEncryptionPadding.OaepSHA256);

        using var loaded = RsaKeyLoader.LoadPrivateKeyFromPem(pem);
        var decoded = RsaDecrypt.DecryptOaepSha256(loaded, Convert.ToBase64String(cipher));
        decoded.Should().Be(secret);
    }

    [Fact]
    public void Loader_rejects_garbage() =>
        FluentActions.Invoking(() => RsaKeyLoader.LoadPrivateKeyFromPem("not a pem"))
            .Should().Throw<Exception>()
            .Which.Should().Match(e => e is CryptographicException || e is ArgumentException);
}

internal static class StringChunk
{
    public static string Chunked(this string s, int width)
    {
        var sb = new StringBuilder(s.Length + s.Length / width);
        for (var i = 0; i < s.Length; i += width)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(s, i, Math.Min(width, s.Length - i));
        }
        return sb.ToString();
    }
}
