using System.Xml;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Enums;

namespace VisuFiscalHub.Infrastructure.Fiscal.XmlBuilder;

internal sealed class NfceXmlBuilder : INfceXmlBuilder
{
    private const string NfeNs = "http://www.portalfiscal.inf.br/nfe";

    private readonly IQrCodeGenerator _qrCodeGenerator;
    private readonly ICertificateEncryptionService _encryptionService;

    public NfceXmlBuilder(
        IQrCodeGenerator qrCodeGenerator,
        ICertificateEncryptionService encryptionService)
    {
        _qrCodeGenerator = qrCodeGenerator;
        _encryptionService = encryptionService;
    }

    public Result<XmlDocument> Construir(DocumentoFiscal documento, Tenant tenant)
    {
        if (tenant.CIdToken is null)
            return Result.Failure<XmlDocument>(
                new Error("XmlBuilder.CIdTokenAusente", "cIdToken não configurado para o Tenant."));

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

        // <dest> (apenas se houver consumidor)
        // Consumidor não está no DocumentoFiscal diretamente nesta versão — omitido conforme spec.

        // <det> (itens)
        for (var i = 0; i < documento.Items.Count; i++)
            infNFe.AppendChild(BuildDet(doc, documento.Items[i], i + 1, tenant.ConfiguracaoFiscal.Crt));

        // <total>
        infNFe.AppendChild(BuildTotal(doc, documento));

        // <transp>
        infNFe.AppendChild(BuildTransp(doc));

        // <pag>
        infNFe.AppendChild(BuildPag(doc, documento));

        // <infAdic>
        var infAdic = doc.CreateElement("infAdic", NfeNs);
        var infCpl = doc.CreateElement("infCpl", NfeNs);
        infCpl.InnerText = "VisuFiscalHub";
        infAdic.AppendChild(infCpl);
        infNFe.AppendChild(infAdic);

        // <infNFeSupl> com QR Code
        if (tenant.Csc is not null)
        {
            var cscResult = _encryptionService.DecryptToString(tenant.Csc);
            if (cscResult.IsSuccess)
            {
                var qrResult = _qrCodeGenerator.Gerar(
                    documento.ChaveAcesso,
                    tenant.ConfiguracaoFiscal.Ambiente,
                    cscResult.Value,
                    ResolverUrlConsulta(tenant.ConfiguracaoFiscal.UfCodigo, tenant.ConfiguracaoFiscal.Ambiente));

                if (qrResult.IsSuccess)
                    nfeEl.AppendChild(BuildInfNFeSupl(doc, qrResult.Value.UrlCompleta, tenant.ConfiguracaoFiscal.UfCodigo, tenant.ConfiguracaoFiscal.Ambiente));
            }
        }

        return Result.Success(doc);
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
        Add("idDest", "1");     // Operação interna
        Add("cMunFG", tenant.Endereco.CodigoMunicipio.ToString());
        Add("tpImp", "4");      // DANFE NFC-e
        Add("tpEmis", "1");     // Emissão normal
        Add("cDV", documento.ChaveAcesso.Valor[^1..]);
        Add("tpAmb", ((int)tenant.ConfiguracaoFiscal.Ambiente).ToString());
        Add("finNFe", "1");     // NF-e normal
        Add("indFinal", "1");   // Consumidor final
        Add("indPres", "1");    // Presencial
        Add("procEmi", "3");    // Emitido por contribuinte — API NF-e
        Add("verProc", "VisuFiscalHub 1.0");

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

        Add("IE", "ISENTO"); // Simples pode não ter IE — ajustar conforme Tenant
        Add("CRT", ((int)tenant.ConfiguracaoFiscal.Crt).ToString());

        return emit;
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
        Add("vTotTrib", "0.00");

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

    private static XmlElement BuildInfNFeSupl(
        XmlDocument doc,
        string qrCodeUrl,
        int ufCodigo,
        AmbienteSefaz ambiente)
    {
        var infSupl = doc.CreateElement("infNFeSupl", NfeNs);

        var qrCode = doc.CreateElement("qrCode", NfeNs);
        qrCode.InnerText = qrCodeUrl;
        infSupl.AppendChild(qrCode);

        var urlFe = doc.CreateElement("urlFe", NfeNs);
        urlFe.InnerText = ResolverUrlConsulta(ufCodigo, ambiente);
        infSupl.AppendChild(urlFe);

        return infSupl;
    }

    private static string ResolverUrlConsulta(int ufCodigo, AmbienteSefaz ambiente)
    {
        // URLs de consulta NFC-e por ambiente
        return ambiente == AmbienteSefaz.Producao
            ? "https://www.nfce.fazenda.sp.gov.br/consulta"
            : "https://www.homologacao.nfce.fazenda.sp.gov.br/consulta";
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
