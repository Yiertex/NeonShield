using System.Security.Cryptography;
using System.Text;

namespace NeonShield.Services;

public static class SecretProtectionService
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NeonShield-Yiertex-VirusTotal");

    public static string Protect(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var plaintext = Encoding.UTF8.GetBytes(value.Trim());
        var encrypted = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    public static string Unprotect(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        try
        {
            var encrypted = Convert.FromBase64String(value);
            var plaintext = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch
        {
            return string.Empty;
        }
    }
}
