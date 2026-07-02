using System.Security.Cryptography;

namespace VisuFiscalHub.Api.Authentication;

/// <summary>
/// HMAC-normalize both sides before comparing to eliminate the length oracle that
/// arises when FixedTimeEquals receives inputs of different lengths.
/// Hashing with a fixed in-process key produces equal-length MACs regardless of
/// input length while preserving constant-time behavior.
/// </summary>
internal static class AdminKeyHelper
{
    private static readonly byte[] _hmacKey = RandomNumberGenerator.GetBytes(32);

    public static bool VerifyAdminKey(string provided, string expected)
    {
        var enc = System.Text.Encoding.UTF8;
        var providedMac = HMACSHA256.HashData(_hmacKey, enc.GetBytes(provided));
        var expectedMac = HMACSHA256.HashData(_hmacKey, enc.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(providedMac, expectedMac);
    }
}
