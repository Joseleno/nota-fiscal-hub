using VisuFiscalHub.Domain.Common;

namespace VisuFiscalHub.Application.Common.Interfaces;

public interface ICertificateEncryptionService
{
    Result<byte[]> Encrypt(byte[] data);
    Result<byte[]> Decrypt(byte[] encrypted);

    Result<byte[]> EncryptString(string text);
    Result<string> DecryptToString(byte[] encrypted);
}
