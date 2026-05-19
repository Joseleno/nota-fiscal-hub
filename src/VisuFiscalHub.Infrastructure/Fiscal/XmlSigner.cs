using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;

namespace VisuFiscalHub.Infrastructure.Fiscal;

internal sealed class XmlSigner
{
    /// <summary>
    /// Assina o documento XML NFC-e conforme ABNT NBR 6030 / NT SEFAZ.
    /// Canonicalização: Exc-C14N; Digest: SHA-1; Assinatura: RSA-SHA1.
    /// </summary>
    public XmlDocument Assinar(XmlDocument xmlDoc, X509Certificate2 certificado, string chaveAcesso)
    {
        var signedXml = new SignedXml(xmlDoc);

        using var rsa = certificado.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("Certificado não possui chave privada RSA.");

        signedXml.SigningKey = rsa;
        signedXml.SignedInfo.SignatureMethod = SignedXml.XmlDsigRSASHA1Url;
        signedXml.SignedInfo.CanonicalizationMethod = SignedXml.XmlDsigExcC14NTransformUrl;

        var reference = new Reference
        {
            Uri = $"#NFe{chaveAcesso}",
            DigestMethod = SignedXml.XmlDsigSHA1Url
        };

        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        reference.AddTransform(new XmlDsigExcC14NTransform());

        signedXml.AddReference(reference);

        // Adiciona o certificado X.509 no bloco KeyInfo
        var keyInfo = new KeyInfo();
        keyInfo.AddClause(new KeyInfoX509Data(certificado));
        signedXml.KeyInfo = keyInfo;

        signedXml.ComputeSignature();

        var xmlSignature = signedXml.GetXml()
            ?? throw new InvalidOperationException("ComputeSignature produziu elemento nulo.");

        // Insere a assinatura ao final do elemento raiz NFeProc ou NFe
        xmlDoc.DocumentElement!.AppendChild(xmlDoc.ImportNode(xmlSignature, true));

        return xmlDoc;
    }
}
