using System.Security.Cryptography;
using System.Text;

namespace CryptoChief.Processing.Rsa;

/// <summary>PEM RSA private key loader. Accepts PKCS#1 and PKCS#8.</summary>
public static class RsaKeyLoader
{
    public static RSA LoadPrivateKeyFromFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var pem = File.ReadAllText(path);
        return LoadPrivateKeyFromPem(pem);
    }

    public static RSA LoadPrivateKeyFromPem(string pem)
    {
        ArgumentNullException.ThrowIfNull(pem);

        var rsa = RSA.Create();
        try
        {
#if NET6_0_OR_GREATER
            rsa.ImportFromPem(pem);
            return rsa;
#else
            var (label, body) = ExtractPem(pem);
            var bytes = Convert.FromBase64String(body);
            switch (label)
            {
                case "RSA PRIVATE KEY":
                    rsa.ImportRSAPrivateKey(bytes, out _);
                    break;
                case "PRIVATE KEY":
                    rsa.ImportPkcs8PrivateKey(bytes, out _);
                    break;
                default:
                    throw new CryptographicException(
                        $"cryptochief: unsupported PEM label \"{label}\" (want RSA PRIVATE KEY or PRIVATE KEY)");
            }
            return rsa;
#endif
        }
        catch
        {
            rsa.Dispose();
            throw;
        }
    }

#if !NET6_0_OR_GREATER
    private static (string Label, string Body) ExtractPem(string pem)
    {
        var lines = pem.Replace("\r\n", "\n").Split('\n');
        string? label = null;
        var sb = new StringBuilder();
        var inBody = false;
        foreach (var l in lines)
        {
            if (l.StartsWith("-----BEGIN ", StringComparison.Ordinal))
            {
                inBody = true;
                label = l.Replace("-----BEGIN ", "").Replace("-----", "").Trim();
                continue;
            }
            if (l.StartsWith("-----END ", StringComparison.Ordinal)) break;
            if (inBody) sb.Append(l.Trim());
        }
        if (label is null)
            throw new CryptographicException("cryptochief: no PEM block found");
        return (label, sb.ToString());
    }
#endif
}
