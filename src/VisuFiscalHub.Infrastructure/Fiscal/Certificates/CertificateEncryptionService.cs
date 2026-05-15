using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Common;

namespace VisuFiscalHub.Infrastructure.Fiscal.Certificates;

internal sealed class CertificateEncryptionService : ICertificateEncryptionService
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public CertificateEncryptionService(IConfiguration configuration)
    {
        var keyBase64 = configuration["CERT:EncryptionKey"]
            ?? throw new InvalidOperationException("CERT:EncryptionKey não configurado.");

        _key = Convert.FromBase64String(keyBase64);

        if (_key.Length != 32)
            throw new InvalidOperationException("CERT:EncryptionKey deve ter exatamente 32 bytes (256 bits).");
    }

    public Result<byte[]> Encrypt(byte[] data)
    {
        try
        {
            // Nonce gerado com RandomNumberGenerator a cada chamada — nunca reutilizar nonce com mesma chave.
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var tag = new byte[TagSize];
            var ciphertext = new byte[data.Length];

            using var aesGcm = new AesGcm(_key, TagSize);
            aesGcm.Encrypt(nonce, data, ciphertext, tag);

            // Layout: [nonce(12)] [tag(16)] [ciphertext]
            var result = new byte[NonceSize + TagSize + ciphertext.Length];
            nonce.CopyTo(result, 0);
            tag.CopyTo(result, NonceSize);
            ciphertext.CopyTo(result, NonceSize + TagSize);

            return Result.Success(result);
        }
        catch (Exception ex)
        {
            return Result.Failure<byte[]>(new Error("Encryption.Failed", ex.Message));
        }
    }

    public Result<byte[]> Decrypt(byte[] encrypted)
    {
        try
        {
            if (encrypted.Length < NonceSize + TagSize)
                return Result.Failure<byte[]>(new Error("Encryption.InvalidData", "Dados criptografados inválidos."));

            var nonce = encrypted[..NonceSize];
            var tag = encrypted[NonceSize..(NonceSize + TagSize)];
            var ciphertext = encrypted[(NonceSize + TagSize)..];

            var plaintext = new byte[ciphertext.Length];

            using var aesGcm = new AesGcm(_key, TagSize);
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);

            return Result.Success(plaintext);
        }
        catch (Exception ex)
        {
            return Result.Failure<byte[]>(new Error("Encryption.Failed", ex.Message));
        }
    }

    public Result<byte[]> EncryptString(string text)
        => Encrypt(Encoding.UTF8.GetBytes(text));

    public Result<string> DecryptToString(byte[] encrypted)
    {
        var result = Decrypt(encrypted);
        if (result.IsFailure)
            return Result.Failure<string>(result.Error);

        return Result.Success(Encoding.UTF8.GetString(result.Value));
    }
}
