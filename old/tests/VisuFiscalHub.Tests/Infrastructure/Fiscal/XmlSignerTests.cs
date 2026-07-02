using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using Shouldly;
using VisuFiscalHub.Infrastructure.Fiscal;

namespace VisuFiscalHub.Tests.Infrastructure.Fiscal;

public class XmlSignerTests
{
    private static readonly string ChaveAcesso = "35090614200167140065125001000001800100000097";

    // Certificado autoassinado gerado em memória — suficiente para testar a estrutura da assinatura.
    private static X509Certificate2 GerarCertificadoTeste()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=TesteSigner",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    private static XmlDocument CriarXmlMinimo()
    {
        var doc = new XmlDocument();
        doc.LoadXml($"""
            <NFe xmlns="http://www.portalfiscal.inf.br/nfe">
              <infNFe Id="NFe{ChaveAcesso}" versao="4.00">
                <ide><mod>65</mod></ide>
              </infNFe>
            </NFe>
            """);
        return doc;
    }

    [Fact]
    public void Assinar_CanonicalizationMethod_DeveSerC14NInclusivo()
    {
        var signer = new XmlSigner();
        var cert = GerarCertificadoTeste();
        var doc = CriarXmlMinimo();

        var signed = signer.Assinar(doc, cert, $"#NFe{ChaveAcesso}");

        var ns = new XmlNamespaceManager(signed.NameTable);
        ns.AddNamespace("ds", "http://www.w3.org/2000/09/xmldsig#");

        var canonMethod = signed.SelectSingleNode(
            "//ds:SignedInfo/ds:CanonicalizationMethod/@Algorithm", ns)?.Value;

        // SEFAZ exige C14N inclusivo — Exc-C14N (http://www.w3.org/2001/10/xml-exc-c14n#) é rejeitado com cStat=704
        canonMethod.ShouldBe(
            "http://www.w3.org/TR/2001/REC-xml-c14n-20010315",
            "CanonicalizationMethod deve ser C14N inclusivo conforme NT SEFAZ");
    }

    [Fact]
    public void Assinar_Transform_DeveSerC14NInclusivo()
    {
        var signer = new XmlSigner();
        var cert = GerarCertificadoTeste();
        var doc = CriarXmlMinimo();

        var signed = signer.Assinar(doc, cert, $"#NFe{ChaveAcesso}");

        var ns = new XmlNamespaceManager(signed.NameTable);
        ns.AddNamespace("ds", "http://www.w3.org/2000/09/xmldsig#");

        // A segunda transform da Reference deve ser C14N inclusivo (a primeira é EnvelopedSignature)
        var transforms = signed.SelectNodes(
            "//ds:Reference/ds:Transforms/ds:Transform/@Algorithm", ns)!;

        var algorithmValues = transforms.Cast<XmlAttribute>().Select(a => a.Value).ToList();

        algorithmValues.ShouldContain(
            "http://www.w3.org/TR/2001/REC-xml-c14n-20010315",
            "Transform da Reference deve incluir C14N inclusivo conforme NT SEFAZ");
    }

    [Fact]
    public void Assinar_SignatureMethod_DeveSerRSASHA1()
    {
        var signer = new XmlSigner();
        var cert = GerarCertificadoTeste();
        var doc = CriarXmlMinimo();

        var signed = signer.Assinar(doc, cert, $"#NFe{ChaveAcesso}");

        var ns = new XmlNamespaceManager(signed.NameTable);
        ns.AddNamespace("ds", "http://www.w3.org/2000/09/xmldsig#");

        var sigMethod = signed.SelectSingleNode(
            "//ds:SignedInfo/ds:SignatureMethod/@Algorithm", ns)?.Value;

        sigMethod.ShouldBe(SignedXml.XmlDsigRSASHA1Url);
    }

    [Fact]
    public void Assinar_ReferenceUri_DeveConterPrefixoNFe()
    {
        var signer = new XmlSigner();
        var cert = GerarCertificadoTeste();
        var doc = CriarXmlMinimo();

        var signed = signer.Assinar(doc, cert, $"#NFe{ChaveAcesso}");

        var ns = new XmlNamespaceManager(signed.NameTable);
        ns.AddNamespace("ds", "http://www.w3.org/2000/09/xmldsig#");

        var uri = signed.SelectSingleNode(
            "//ds:Reference/@URI", ns)?.Value;

        uri.ShouldBe($"#NFe{ChaveAcesso}");
    }

    [Fact]
    public void Assinar_XmlAssinado_DevePassarVerificacaoCheckSignature()
    {
        var signer = new XmlSigner();
        var cert = GerarCertificadoTeste();
        var doc = CriarXmlMinimo();

        var signed = signer.Assinar(doc, cert, $"#NFe{ChaveAcesso}");

        var signedXml = new SignedXml(signed);
        var ns = new XmlNamespaceManager(signed.NameTable);
        ns.AddNamespace("ds", "http://www.w3.org/2000/09/xmldsig#");
        var sigNode = (XmlElement)signed.SelectSingleNode("//ds:Signature", ns)!;
        signedXml.LoadXml(sigNode);

        signedXml.CheckSignature(cert, verifySignatureOnly: true).ShouldBeTrue(
            "A assinatura produzida deve ser verificável com o certificado usado");
    }
}
