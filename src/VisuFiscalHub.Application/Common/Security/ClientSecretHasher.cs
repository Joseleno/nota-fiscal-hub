using System.Security.Cryptography;

namespace VisuFiscalHub.Application.Common.Security;

public static class ClientSecretHasher
{
    private const int Iterations = 600_000;
    private const int SaltSize = 32;
    private const int HashSize = 32;

    public static string DerivarHash(string secret)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password: secret,
            salt: salt,
            iterations: Iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: HashSize);
        return Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(hash);
    }

    public static bool VerificarHash(string secret, string storedHash)
    {
        var parts = storedHash.Split(':');
        if (parts.Length != 2)
            return false;

        byte[] salt;
        byte[] expectedHash;
        try
        {
            salt = Convert.FromBase64String(parts[0]);
            expectedHash = Convert.FromBase64String(parts[1]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length != SaltSize || expectedHash.Length != HashSize)
            return false;

        var actualHash = Rfc2898DeriveBytes.Pbkdf2(
            password: secret,
            salt: salt,
            iterations: Iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: HashSize);

        // Constant-time comparison to prevent timing attacks.
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
