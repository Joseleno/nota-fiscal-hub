using System.Xml;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.ValueObjects;

namespace VisuFiscalHub.Infrastructure.Fiscal.XmlBuilder;

internal sealed class FiscalDocumentXmlBuilder : IFiscalDocumentXmlBuilder
{
    private const string NfeNs = "http://www.portalfiscal.inf.br/nfe";

    private readonly IQrCodeGenerator _qrCodeGenerator;
    private readonly ICertificateEncryptionService _encryptionService;

    public FiscalDocumentXmlBuilder(
        IQrCodeGenerator qrCodeGenerator,
        ICertificateEncryptionService encryptionService)
    {
        _qrCodeGenerator = qrCodeGenerator;
        _encryptionService = encryptionService;
    }

    public Result<XmlDocument> Construir(DocumentoFiscal documento, Tenant tenant)
    {
        return documento.Tipo == TipoDocumento.NFe
            ? ConstruirNfe(documento, tenant)
            : ConstruirNfce(documento, tenant);
    }

    private Result<XmlDocument> ConstruirNfce(DocumentoFiscal documento, Tenant tenant)
    {
        if (tenant.CIdToken is null)
            return Result.Failure<XmlDocument>(
                new Error("XmlBuilder.CIdTokenAusente", "cIdToken não configurado para o Tenant."));

        // Verificação antecipada do CSC evita construir todo o XML para depois descartar em caso de falha.
        if (tenant.Csc is null)
            return Result.Failure<XmlDocument>(
                new Error("XmlBuilder.CscAusente", "CSC não configurado para o Tenant."));

        var doc = new XmlDocument { PreserveWhitespace = false };
        var decl = doc.CreateXmlDeclaration("1.0", "UTF-8", null);
        doc.AppendChild(decl);

        var nfeEl = doc.CreateElement("NFe", NfeNs);
        doc.AppendChild(nfeEl);

        // <infNFe> com Id para a assinatura digital
        var infNFe = doc.CreateElement("infNFe", NfeNs);
        infNFe.SetAttribute("Id", $"NFe{documento.ChaveAcesso.Valor}");
        infNFe.SetAttribute("versao", "4.00");
        nfeEl.AppendChild(infNFe);

        // Fuso horário da UF do Tenant
        UfFusoHorario.Mapa.TryGetValue(tenant.ConfiguracaoFiscal.UfCodigo, out var offset);
        var dhEmi = documento.CreatedAt.ToOffset(offset).ToString("yyyy-MM-ddTHH:mm:sszzz");

        // <ide>
        var ide = BuildIde(doc, documento, tenant, dhEmi);
        infNFe.AppendChild(ide);

        // <emit>
        infNFe.AppendChild(BuildEmit(doc, tenant));

        // <dest> (apenas se houver CPF — obrigatório acima de R$ 10.000)
        if (documento.CpfConsumidor is not null)
            infNFe.AppendChild(BuildDest(doc, documento.CpfConsumidor, documento.NomeConsumidor));

        // <det> (itens)
        for (var i = 0; i < documento.Items.Count; i++)
            infNFe.AppendChild(BuildDet(doc, documento.Items[i], i + 1, tenant.ConfiguracaoFiscal.Crt));

        // <total>
        infNFe.AppendChild(BuildTotal(doc, documento));

        // <transp>
        infNFe.AppendChild(BuildTransp(doc));

        // <pag>
        infNFe.AppendChild(BuildPag(doc, documento));

        // <infNFeSupl> com QR Code (CSC garantido não-nulo pelo guard no topo do método)
        var cscResult = _encryptionService.DecryptToString(tenant.Csc!);
        if (cscResult.IsFailure)
            return Result.Failure<XmlDocument>(cscResult.Error);

        var urlConsulta = ResolverUrlConsulta(tenant.ConfiguracaoFiscal.UfCodigo, tenant.ConfiguracaoFiscal.Ambiente);
        var qrResult = _qrCodeGenerator.Gerar(
            documento.ChaveAcesso,
            tenant.ConfiguracaoFiscal.Ambiente,
            cscResult.Value,
            urlConsulta);

        if (qrResult.IsFailure)
            return Result.Failure<XmlDocument>(qrResult.Error);

        infNFe.AppendChild(BuildInfNFeSupl(doc, qrResult.Value.UrlCompleta, urlConsulta));

        return Result.Success(doc);
    }

    private Result<XmlDocument> ConstruirNfe(DocumentoFiscal documento, Tenant tenant)
    {
        var doc = new XmlDocument { PreserveWhitespace = false };
        var decl = doc.CreateXmlDeclaration("1.0", "UTF-8", null);
        doc.AppendChild(decl);

        var nfeEl = doc.CreateElement("NFe", NfeNs);
        doc.AppendChild(nfeEl);

        var infNFe = doc.CreateElement("infNFe", NfeNs);
        infNFe.SetAttribute("Id", $"NFe{documento.ChaveAcesso.Valor}");
        infNFe.SetAttribute("versao", "4.00");
        nfeEl.AppendChild(infNFe);

        UfFusoHorario.Mapa.TryGetValue(tenant.ConfiguracaoFiscal.UfCodigo, out var offset);
        var dhEmi = documento.CreatedAt.ToOffset(offset).ToString("yyyy-MM-ddTHH:mm:sszzz");

        infNFe.AppendChild(BuildIdeNfe(doc, documento, tenant, dhEmi));
        infNFe.AppendChild(BuildEmit(doc, tenant));
        infNFe.AppendChild(BuildDestNfe(doc, documento.NfeDestinatario!));
        for (var i = 0; i < documento.Items.Count; i++)
            infNFe.AppendChild(BuildDet(doc, documento.Items[i], i + 1, tenant.ConfiguracaoFiscal.Crt));
        infNFe.AppendChild(BuildTotal(doc, documento));
        infNFe.AppendChild(BuildTranspNfe(doc, documento));
        infNFe.AppendChild(BuildPag(doc, documento));

        return Result.Success(doc);
    }

    private static XmlElement BuildIdeNfe(XmlDocument doc, DocumentoFiscal documento, Tenant tenant, string dhEmi)
    {
        var ide = doc.CreateElement("ide", NfeNs);
        void Add(string tag, string val) { var el = doc.CreateElement(tag, NfeNs); el.InnerText = val; ide.AppendChild(el); }

        Add("cUF", tenant.ConfiguracaoFiscal.UfCodigo.ToString());
        Add("cNF", documento.ChaveAcesso.Valor[35..43]);
        Add("natOp", documento.NatOp ?? "Venda de Mercadoria");
        Add("mod", "55");
        Add("serie", documento.Serie.PadLeft(3, '0'));
        Add("nNF", documento.Numero.ToString().PadLeft(9, '0'));
        Add("dhEmi", dhEmi);
        Add("dhSaiEnt", dhEmi);
        Add("tpNF", "1");
        var primeiroItem = documento.Items.FirstOrDefault();
        var idDest = primeiroItem?.Produto.CfopSaida.StartsWith("6") == true ? "2" : "1";
        Add("idDest", idDest);
        Add("cMunFG", tenant.Endereco.CodigoMunicipio.ToString());
        Add("tpImp", "1");
        Add("tpEmis", "1");
        Add("cDV", documento.ChaveAcesso.Valor[^1..]);
        Add("tpAmb", ((int)tenant.ConfiguracaoFiscal.Ambiente).ToString());
        Add("finNFe", "1");
        Add("indFinal", "0");
        Add("indPres", documento.IndPresenca.ToString());
        Add("procEmi", "0");
        Add("verProc", "VisuFiscalHub 1.0");

        return ide;
    }

    private static XmlElement BuildDestNfe(XmlDocument doc, NfeDestinatario dest)
    {
        var destEl = doc.CreateElement("dest", NfeNs);
        void Add(string tag, string val) { var el = doc.CreateElement(tag, NfeNs); el.InnerText = val; destEl.AppendChild(el); }
        void AddTo(XmlElement parent, string tag, string val) { var el = doc.CreateElement(tag, NfeNs); el.InnerText = val; parent.AppendChild(el); }

        if (dest.CnpjOuCpf.Length == 14)
            Add("CNPJ", dest.CnpjOuCpf);
        else
            Add("CPF", dest.CnpjOuCpf);

        Add("xNome", dest.RazaoSocial);

        var enderDest = doc.CreateElement("enderDest", NfeNs);
        AddTo(enderDest, "xLgr", dest.Logradouro);
        AddTo(enderDest, "nro", dest.Numero);
        if (!string.IsNullOrEmpty(dest.Complemento)) AddTo(enderDest, "xCpl", dest.Complemento);
        AddTo(enderDest, "xBairro", dest.Bairro);
        AddTo(enderDest, "cMun", dest.CodigoMunicipio);
        AddTo(enderDest, "xMun", dest.Municipio);
        AddTo(enderDest, "UF", dest.Uf);
        AddTo(enderDest, "CEP", dest.Cep);
        AddTo(enderDest, "cPais", "1058");
        AddTo(enderDest, "xPais", "Brasil");
        destEl.AppendChild(enderDest);

        Add("indIEDest", dest.IndIeDest.ToString());
        if (dest.Ie is not null) Add("IE", dest.Ie);
        if (dest.Email is not null) Add("email", dest.Email);

        return destEl;
    }

    private static XmlElement BuildTranspNfe(XmlDocument doc, DocumentoFiscal documento)
    {
        var transp = doc.CreateElement("transp", NfeNs);
        var modFrete = doc.CreateElement("modFrete", NfeNs);
        modFrete.InnerText = documento.ModFrete.ToString();
        transp.AppendChild(modFrete);
        return transp;
    }

    private static XmlElement BuildIde(
        XmlDocument doc,
        DocumentoFiscal documento,
        Tenant tenant,
        string dhEmi)
    {
        var ide = doc.CreateElement("ide", NfeNs);
        void Add(string tag, string val) { var el = doc.CreateElement(tag, NfeNs); el.InnerText = val; ide.AppendChild(el); }

        Add("cUF", tenant.ConfiguracaoFiscal.UfCodigo.ToString());
        Add("cNF", documento.ChaveAcesso.Valor[35..43]); // posições 36-43 na chave (0-based: [35..43])
        Add("natOp", "VENDA AO CONSUMIDOR");
        Add("mod", "65");
        Add("serie", tenant.ConfiguracaoFiscal.Serie.PadLeft(3, '0'));
        Add("nNF", documento.Numero.ToString().PadLeft(9, '0'));
        Add("dhEmi", dhEmi);
        Add("tpNF", "1");       // NF de saída
        var primeiroItem = documento.Items.FirstOrDefault();
        var idDest = primeiroItem?.Produto.CfopSaida.StartsWith("6") == true ? "2" : "1";
        Add("idDest", idDest);
        Add("cMunFG", tenant.Endereco.CodigoMunicipio.ToString());
        Add("tpImp", "4");      // DANFE NFC-e
        Add("tpEmis", "1");     // Emissão normal
        Add("cDV", documento.ChaveAcesso.Valor[^1..]);
        Add("tpAmb", ((int)tenant.ConfiguracaoFiscal.Ambiente).ToString());
        Add("finNFe", "1");     // NF-e normal
        Add("indFinal", "1");   // Consumidor final
        Add("indPres", ((int)documento.IndPresenca).ToString());
        Add("procEmi", "3");    // Emitido por contribuinte — API NF-e
        Add("verProc", "VisuFiscalHub 1.0");
        // cIdToken não é elemento do schema NF-e 4.0 — é usado apenas para compor a URL do QR Code

        return ide;
    }

    private static XmlElement BuildEmit(XmlDocument doc, Tenant tenant)
    {
        var emit = doc.CreateElement("emit", NfeNs);
        void Add(string tag, string val) { var el = doc.CreateElement(tag, NfeNs); el.InnerText = val; emit.AppendChild(el); }
        void AddChild(XmlElement parent, string tag, string val) { var el = doc.CreateElement(tag, NfeNs); el.InnerText = val; parent.AppendChild(el); }

        Add("CNPJ", tenant.Cnpj.Valor);
        Add("xNome", tenant.RazaoSocial.Length > 60 ? tenant.RazaoSocial[..60] : tenant.RazaoSocial);
        if (tenant.NomeFantasia is not null)
            Add("xFant", tenant.NomeFantasia.Length > 60 ? tenant.NomeFantasia[..60] : tenant.NomeFantasia);

        var end = doc.CreateElement("enderEmit", NfeNs);
        AddChild(end, "xLgr", tenant.Endereco.Logradouro);
        AddChild(end, "nro", tenant.Endereco.Numero);
        if (tenant.Endereco.Complemento is not null) AddChild(end, "xCpl", tenant.Endereco.Complemento);
        AddChild(end, "xBairro", tenant.Endereco.Bairro);
        AddChild(end, "cMun", tenant.Endereco.CodigoMunicipio.ToString());
        AddChild(end, "xMun", tenant.Endereco.Municipio);
        AddChild(end, "UF", tenant.Endereco.Uf);
        AddChild(end, "CEP", tenant.Endereco.Cep);
        AddChild(end, "cPais", tenant.Endereco.CodigoPais);
        AddChild(end, "xPais", "BRASIL");
        if (tenant.Endereco.Telefone is not null) AddChild(end, "fone", tenant.Endereco.Telefone);
        emit.AppendChild(end);

        Add("IE", tenant.ConfiguracaoFiscal.InscricaoEstadual ?? "ISENTO");
        Add("CRT", ((int)tenant.ConfiguracaoFiscal.Crt).ToString());

        return emit;
    }

    private static XmlElement BuildDest(XmlDocument doc, string cpf, string? nome)
    {
        var dest = doc.CreateElement("dest", NfeNs);

        var Add = (string tag, string val) =>
        {
            var el = doc.CreateElement(tag, NfeNs);
            el.InnerText = val;
            dest.AppendChild(el);
        };

        Add("CPF", cpf);
        if (!string.IsNullOrWhiteSpace(nome))
            Add("xNome", nome.Length > 60 ? nome[..60] : nome);
        Add("indIEDest", "9");   // 9 = não contribuinte (consumidor final NFC-e)

        return dest;
    }

    private static XmlElement BuildDet(
        XmlDocument doc,
        ItemDocumento item,
        int nItem,
        RegimeTributario crt)
    {
        var det = doc.CreateElement("det", NfeNs);
        det.SetAttribute("nItem", nItem.ToString());

        void Add(XmlElement parent, string tag, string val)
        {
            var el = doc.CreateElement(tag, NfeNs);
            el.InnerText = val;
            parent.AppendChild(el);
        }

        var prod = doc.CreateElement("prod", NfeNs);
        Add(prod, "cProd", item.Produto.CodigoProduto);
        Add(prod, "cEAN", "SEM GTIN");
        Add(prod, "xProd", item.Produto.Descricao);
        Add(prod, "NCM", item.Produto.Ncm);
        if (item.Produto.Cest is not null) Add(prod, "CEST", item.Produto.Cest);
        Add(prod, "CFOP", item.Produto.CfopSaida);
        Add(prod, "uCom", item.Produto.UnidadeComercial);
        Add(prod, "qCom", item.Produto.Quantidade.ToString("F4"));
        Add(prod, "vUnCom", item.Produto.ValorUnitario.ToString("F10"));
        Add(prod, "vProd", item.Produto.ValorBruto.ToString("F2"));
        Add(prod, "cEANTrib", "SEM GTIN");
        Add(prod, "uTrib", item.Produto.UnidadeComercial);
        Add(prod, "qTrib", item.Produto.Quantidade.ToString("F4"));
        Add(prod, "vUnTrib", item.Produto.ValorUnitario.ToString("F10"));
        if (item.Produto.ValorDesconto > 0)
            Add(prod, "vDesc", item.Produto.ValorDesconto.ToString("F2"));
        Add(prod, "indTot", "1");
        det.AppendChild(prod);

        // <imposto>
        var imposto = doc.CreateElement("imposto", NfeNs);
        var icms = doc.CreateElement("ICMS", NfeNs);

        var t = item.Tributo;
        if (crt == RegimeTributario.RegimeNormal)
        {
            // CST ICMS
            var icmsTagName = GetIcmsCstTagName((CstIcms)t.CsosnOuCst);
            var icmsDetail = doc.CreateElement(icmsTagName, NfeNs);
            Add(icmsDetail, "orig", ((int)item.Produto.OrigemMercadoria).ToString());
            Add(icmsDetail, "CST", t.CsosnOuCst.ToString("D2"));
            if (t.BaseCalculoIcms > 0)
            {
                Add(icmsDetail, "modBC", "3");
                Add(icmsDetail, "vBC", t.BaseCalculoIcms.ToString("F2"));
                Add(icmsDetail, "pICMS", t.AliquotaIcms.ToString("F2"));
                Add(icmsDetail, "vICMS", t.ValorIcms.ToString("F2"));
            }
            icms.AppendChild(icmsDetail);
        }
        else
        {
            // CSOSN Simples Nacional
            var icmsTagName = GetIcmsCsosnTagName((CSOSN)t.CsosnOuCst);
            var icmsDetail = doc.CreateElement(icmsTagName, NfeNs);
            Add(icmsDetail, "orig", ((int)item.Produto.OrigemMercadoria).ToString());
            Add(icmsDetail, "CSOSN", t.CsosnOuCst.ToString());
            if (t.BaseCalculoIcms > 0)
            {
                Add(icmsDetail, "modBC", "3");
                Add(icmsDetail, "vBC", t.BaseCalculoIcms.ToString("F2"));
                Add(icmsDetail, "pICMS", t.AliquotaIcms.ToString("F2"));
                Add(icmsDetail, "vICMS", t.ValorIcms.ToString("F2"));
            }
            icms.AppendChild(icmsDetail);
        }
        imposto.AppendChild(icms);

        // PIS
        var pis = doc.CreateElement("PIS", NfeNs);
        if (t.ValorPis > 0)
        {
            var pisAliq = doc.CreateElement("PISAliq", NfeNs);
            Add(pisAliq, "CST", ((int)t.CstPis).ToString("D2"));
            Add(pisAliq, "vBC", t.BaseCalculoPis.ToString("F2"));
            Add(pisAliq, "pPIS", t.AliquotaPis.ToString("F4"));
            Add(pisAliq, "vPIS", t.ValorPis.ToString("F2"));
            pis.AppendChild(pisAliq);
        }
        else
        {
            var pisNt = doc.CreateElement("PISNT", NfeNs);
            Add(pisNt, "CST", ((int)t.CstPis).ToString("D2"));
            pis.AppendChild(pisNt);
        }
        imposto.AppendChild(pis);

        // COFINS
        var cofins = doc.CreateElement("COFINS", NfeNs);
        if (t.ValorCofins > 0)
        {
            var cofinsAliq = doc.CreateElement("COFINSAliq", NfeNs);
            Add(cofinsAliq, "CST", ((int)t.CstCofins).ToString("D2"));
            Add(cofinsAliq, "vBC", t.BaseCalculoCofins.ToString("F2"));
            Add(cofinsAliq, "pCOFINS", t.AliquotaCofins.ToString("F4"));
            Add(cofinsAliq, "vCOFINS", t.ValorCofins.ToString("F2"));
            cofins.AppendChild(cofinsAliq);
        }
        else
        {
            var cofinsNt = doc.CreateElement("COFINSNT", NfeNs);
            Add(cofinsNt, "CST", ((int)t.CstCofins).ToString("D2"));
            cofins.AppendChild(cofinsNt);
        }
        imposto.AppendChild(cofins);

        det.AppendChild(imposto);
        return det;
    }

    private static XmlElement BuildTotal(XmlDocument doc, DocumentoFiscal documento)
    {
        var total = doc.CreateElement("total", NfeNs);
        var icmsTot = doc.CreateElement("ICMSTot", NfeNs);

        void Add(string tag, string val)
        {
            var el = doc.CreateElement(tag, NfeNs);
            el.InnerText = val;
            icmsTot.AppendChild(el);
        }

        var vBC = documento.Items.Sum(i => i.Tributo.BaseCalculoIcms);
        var vICMS = documento.Items.Sum(i => i.Tributo.ValorIcms);
        var vProd = documento.Items.Sum(i => i.Produto.ValorBruto);
        var vDesc = documento.Items.Sum(i => i.Produto.ValorDesconto);
        var vNF = documento.Items.Sum(i => i.ValorTotal);
        var vTotTrib = documento.Items.Sum(i => i.Tributo.ValorIcms + i.Tributo.ValorPis + i.Tributo.ValorCofins);

        Add("vBC", vBC.ToString("F2"));
        Add("vICMS", vICMS.ToString("F2"));
        Add("vICMSDeson", "0.00");
        Add("vFCP", "0.00");
        Add("vBCST", "0.00");
        Add("vST", "0.00");
        Add("vFCPST", "0.00");
        Add("vFCPSTRet", "0.00");
        Add("vProd", vProd.ToString("F2"));
        Add("vFrete", "0.00");
        Add("vSeg", "0.00");
        Add("vDesc", vDesc.ToString("F2"));
        Add("vII", "0.00");
        Add("vIPI", "0.00");
        Add("vIPIDevol", "0.00");
        Add("vPIS", documento.Items.Sum(i => i.Tributo.ValorPis).ToString("F2"));
        Add("vCOFINS", documento.Items.Sum(i => i.Tributo.ValorCofins).ToString("F2"));
        Add("vOutro", "0.00");
        Add("vNF", vNF.ToString("F2"));
        Add("vTotTrib", vTotTrib.ToString("F2"));

        total.AppendChild(icmsTot);
        return total;
    }

    private static XmlElement BuildTransp(XmlDocument doc)
    {
        var transp = doc.CreateElement("transp", NfeNs);
        var modFrete = doc.CreateElement("modFrete", NfeNs);
        modFrete.InnerText = "9"; // Sem frete
        transp.AppendChild(modFrete);
        return transp;
    }

    private static XmlElement BuildPag(XmlDocument doc, DocumentoFiscal documento)
    {
        var pag = doc.CreateElement("pag", NfeNs);

        foreach (var pagamento in documento.Pagamentos)
        {
            var detPag = doc.CreateElement("detPag", NfeNs);
            var tPag = doc.CreateElement("tPag", NfeNs);
            tPag.InnerText = ((int)pagamento.TipoPagamento).ToString("D2");
            var vPag = doc.CreateElement("vPag", NfeNs);
            vPag.InnerText = pagamento.Valor.ToString("F2");
            detPag.AppendChild(tPag);
            detPag.AppendChild(vPag);
            pag.AppendChild(detPag);
        }

        return pag;
    }

    private static XmlElement BuildInfNFeSupl(XmlDocument doc, string qrCodeUrl, string urlConsulta)
    {
        var infSupl = doc.CreateElement("infNFeSupl", NfeNs);

        var qrCode = doc.CreateElement("qrCode", NfeNs);
        qrCode.InnerText = qrCodeUrl;
        infSupl.AppendChild(qrCode);

        var urlFe = doc.CreateElement("urlFe", NfeNs);
        urlFe.InnerText = urlConsulta;
        infSupl.AppendChild(urlFe);

        return infSupl;
    }

    // URLs de consulta NFC-e por UF (IBGE). Fonte: Portais estaduais NFC-e / NT SEFAZ.
    // Estados sem URL de homologação distinta usam a mesma URL de produção (comportamento confirmado).
    private static string ResolverUrlConsulta(int ufCodigo, AmbienteSefaz ambiente)
    {
        var p = ambiente == AmbienteSefaz.Producao;
        return ufCodigo switch
        {
            // Região Norte
            11 => "https://www.nfce.sefin.ro.gov.br/nfce/consulta",                                                               // RO — URL única p/ prod e hom
            12 => "https://www.sefaznet.ac.gov.br/nfce/consulta",                                                                 // AC — URL única
            13 => p ? "https://systems.sefaz.am.gov.br/nfceweb/consultarNFCe.html"    : "https://systems.sefaz.am.gov.br/nfceweb-hom/consultarNFCe.html",    // AM
            14 => p ? "https://www.sefaz.rr.gov.br/nfce/consulta.do"                  : "https://www.sefaz.rr.gov.br/nfce/consulta.do",                      // RR — URL única
            15 => p ? "https://appnfc.sefa.pa.gov.br/portal/view/consultas/nfce/consultaNFCe.seam" : "https://appnfchom.sefa.pa.gov.br/portal/view/consultas/nfce/consultaNFCe.seam", // PA
            16 => p ? "https://www.sefaz.ap.gov.br/nfce/consulta.do"                  : "https://www.sefaz.ap.gov.br/nfce/consulta.do",                      // AP — URL única
            17 => p ? "https://www.sefaz.to.gov.br/nfce/consulta.jsf"                 : "https://homologacao.sefaz.to.gov.br/nfce/consulta.jsf",             // TO

            // Região Nordeste
            21 => p ? "https://www.nfce.sefaz.ma.gov.br/portal/consultaNFCe.do"       : "https://homologacao.nfce.sefaz.ma.gov.br/portal/consultaNFCe.do",   // MA
            22 => p ? "https://www.sefaz.pi.gov.br/nfce/consulta.do"                  : "https://www.sefaz.pi.gov.br/nfce/consulta.do",                      // PI — URL única
            23 => p ? "https://iobot.sefaz.ce.gov.br/nfce/consulta"                   : "https://iobot.sefaz.ce.gov.br/nfce/consulta",                       // CE — URL única
            24 => p ? "https://nfce.set.rn.gov.br/portalDFE/NFCe/consultaNFCe.aspx"   : "https://nfce.set.rn.gov.br/portalDFE/NFCe/consultaNFCe.aspx",       // RN — URL única
            25 => p ? "https://www.receita.pb.gov.br/nfce"                             : "https://www.receita.pb.gov.br/nfce",                                // PB — URL única
            26 => p ? "https://nfce.sefaz.pe.gov.br/nfce-web/consultarNFCe"           : "https://nfcehomolog.sefaz.pe.gov.br/nfce-web/consultarNFCe",        // PE
            27 => p ? "https://nfce.sefaz.al.gov.br/consultaNFCe.htm"                 : "https://nfce.sefaz.al.gov.br/consultaNFCe.htm",                     // AL — URL única
            28 => p ? "https://www.nfe.se.gov.br/portal/exibirListaConsultaNFCe.do"   : "https://www.nfe.se.gov.br/portal/exibirListaConsultaNFCe.do",        // SE — URL única
            29 => p ? "https://nfe.sefaz.ba.gov.br/servicos/nfce/default.aspx"        : "https://hnfe.sefaz.ba.gov.br/servicos/nfce/default.aspx",           // BA

            // Região Sudeste
            31 => p ? "https://nfce.fazenda.mg.gov.br/portalnfce"                     : "https://hnfce.fazenda.mg.gov.br/portalnfce",                        // MG
            32 => p ? "https://app.sefaz.es.gov.br/ConsultaNFCe"                      : "https://app.sefaz.es.gov.br/ConsultaNFCe",                          // ES — URL única
            33 => p ? "https://www.nfce.fazenda.rj.gov.br/consulta"                   : "https://www.homologacao.nfce.fazenda.rj.gov.br/consulta",           // RJ
            35 => p ? "https://www.nfce.fazenda.sp.gov.br/consulta"                   : "https://www.homologacao.nfce.fazenda.sp.gov.br/consulta",           // SP

            // Região Sul
            41 => p ? "https://www.nfce.pr.gov.br/nfce/consulta"                      : "https://www.homologacao.nfce.pr.gov.br/nfce/consulta",              // PR
            42 => p ? "https://sat.sef.sc.gov.br/tax.NET/Sat.NFCe.Consulta.aspx"      : "https://hom.sat.sef.sc.gov.br/tax.NET/Sat.NFCe.Consulta.aspx",      // SC
            43 => p ? "https://www.sefaz.rs.gov.br/NFCE/NFCE-COM.aspx"               : "https://www.sefaz.rs.gov.br/NFCE/NFCE-COM.aspx",                    // RS — URL única

            // Região Centro-Oeste + DF
            50 => p ? "https://www.nfce.fazenda.ms.gov.br/portal/"                    : "https://www.homologacao.nfce.fazenda.ms.gov.br/portal/",            // MS
            51 => p ? "https://www.sefaz.mt.gov.br/nfce/consultanfce"                 : "https://homologacao.sefaz.mt.gov.br/nfce/consultanfce",             // MT
            52 => p ? "https://nfce.sefaz.go.gov.br/pages/consulta-nfce.jsf"          : "https://homologacao.nfce.sefaz.go.gov.br/pages/consulta-nfce.jsf",  // GO
            53 => p ? "https://www.nfe.fazenda.gov.br/portal/consultaRecaptcha.aspx"  : "https://hom.nfe.fazenda.gov.br/portal/consultaRecaptcha.aspx",      // DF

            // Fallback explícito: lança para que o problema apareça em tempo de emissão
            _ => throw new InvalidOperationException($"Código IBGE de UF não mapeado para URL NFC-e: {ufCodigo}.")
        };
    }

    private static string GetIcmsCsosnTagName(CSOSN csosn) => csosn switch
    {
        CSOSN.Csosn101 => "ICMSSN101",
        CSOSN.Csosn102 => "ICMSSN102",
        CSOSN.Csosn103 => "ICMSSN103",
        CSOSN.Csosn201 => "ICMSSN201",
        CSOSN.Csosn202 => "ICMSSN202",
        CSOSN.Csosn203 => "ICMSSN203",
        CSOSN.Csosn300 => "ICMSSN300",
        CSOSN.Csosn400 => "ICMSSN400",
        CSOSN.Csosn500 => "ICMSSN500",
        CSOSN.Csosn900 => "ICMSSN900",
        _ => "ICMSSN400"
    };

    private static string GetIcmsCstTagName(CstIcms cst) => cst switch
    {
        CstIcms.Cst00 => "ICMS00",
        CstIcms.Cst10 => "ICMS10",
        CstIcms.Cst20 => "ICMS20",
        CstIcms.Cst30 => "ICMS30",
        CstIcms.Cst40 => "ICMS40",
        CstIcms.Cst41 => "ICMS41",
        CstIcms.Cst50 => "ICMS50",
        CstIcms.Cst51 => "ICMS51",
        CstIcms.Cst60 => "ICMS60",
        CstIcms.Cst70 => "ICMS70",
        CstIcms.Cst90 => "ICMS90",
        _ => "ICMS40"
    };
}
