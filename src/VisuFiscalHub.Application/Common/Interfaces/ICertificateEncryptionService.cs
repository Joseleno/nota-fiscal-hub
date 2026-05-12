namespace VisuFiscalHub.Application.Common.Interfaces;

public interface ICertificateEncryptionService
{
    byte[] Encrypt(byte[] data);
    byte[] Decrypt(byte[] encrypted);

    byte[] EncryptString(string text);
    string DecryptToString(byte[] encrypted);
}
