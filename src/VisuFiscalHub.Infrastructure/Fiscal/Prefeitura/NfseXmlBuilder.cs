using System.Xml;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Enums;

namespace VisuFiscalHub.Infrastructure.Fiscal.Prefeitura;

/// <summary>
/// Constrói o XML de RPS (Recibo Provisório de Serviço) ABRASF v2.04.
/// </summary>
internal sealed class NfseXmlBuilder : INfseXmlBuilder
{
    private const string NfseNs = "http://www.abrasf.org.br/nfse.xsd";

    public Result<XmlDocument> ConstruirRps(DocumentoFiscal documento, Tenant tenant)
    {
        if (documento.Tipo != TipoDocumento.NFSe)
            return Result.Failure<XmlDocument>(new Error("NfseXml.TipoInvalido",
                $"NfseXmlBuilder recebeu documento do tipo {documento.Tipo} — apenas NFSe é suportado."));

        if (documento.Tomador is null)
            return Result.Failure<XmlDocument>(new Error("NfseXml.TomadorAusente",
                "Tomador é obrigatório para emissão de NFS-e."));

        if (documento.ServicoNfse is null)
            return Result.Failure<XmlDocument>(new Error("NfseXml.ServicoAusente",
                "ServicoNfse é obrigatório para emissão de NFS-e."));

        if (string.IsNullOrWhiteSpace(tenant.ConfiguracaoFiscal.InscricaoMunicipal))
            return Result.Failure<XmlDocument>(new Error("NfseXml.InscricaoMunicipalAusente",
                "InscricaoMunicipal é obrigatória na ConfiguracaoFiscal do tenant para emissão de NFS-e."));

        var doc = new XmlDocument();
        var root = doc.CreateElement("GerarNfseEnvio", NfseNs);
        doc.AppendChild(root);

        var loteRps = doc.CreateElement("LoteRps", NfseNs);
        loteRps.SetAttribute("versao", "2.04");
        root.AppendChild(loteRps);

        AppendElement(doc, loteRps, "NumeroLote", documento.Id.Value.ToString("N")[..15]);
        AppendElement(doc, loteRps, "CpfCnpj", tenant.Cnpj.Valor);
        AppendElement(doc, loteRps, "InscricaoMunicipal", tenant.ConfiguracaoFiscal.InscricaoMunicipal!);
        AppendElement(doc, loteRps, "QuantidadeRps", "1");

        var listaRps = doc.CreateElement("ListaRps", NfseNs);
        loteRps.AppendChild(listaRps);

        var rps = doc.CreateElement("Rps", NfseNs);
        listaRps.AppendChild(rps);

        var infRps = doc.CreateElement("InfRps", NfseNs);
        infRps.SetAttribute("Id", $"Rps{documento.Id.Value:N}");
        rps.AppendChild(infRps);

        var identificacaoRps = doc.CreateElement("IdentificacaoRps", NfseNs);
        infRps.AppendChild(identificacaoRps);
        AppendElement(doc, identificacaoRps, "Numero", documento.Numero.ToString());
        AppendElement(doc, identificacaoRps, "Serie", tenant.ConfiguracaoFiscal.Serie);
        AppendElement(doc, identificacaoRps, "Tipo", "1");

        AppendElement(doc, infRps, "DataEmissao", documento.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss"));
        AppendElement(doc, infRps, "NaturezaOperacao", "1");
        AppendElement(doc, infRps, "OptanteSimplesNacional",
            tenant.ConfiguracaoFiscal.Crt == RegimeTributario.SimplesNacional ? "1" : "2");
        AppendElement(doc, infRps, "IncentivadoCultural", "2");
        AppendElement(doc, infRps, "Status", "1");

        var servicos = doc.CreateElement("Servicos", NfseNs);
        infRps.AppendChild(servicos);

        var servico = doc.CreateElement("Servico", NfseNs);
        servicos.AppendChild(servico);

        var valores = doc.CreateElement("Valores", NfseNs);
        servico.AppendChild(valores);

        var svc = documento.ServicoNfse;
        AppendElement(doc, valores, "ValorServicos", FormatDecimal(svc.BaseCalculoIss));
        if (svc.ValorDeducoes.HasValue)
            AppendElement(doc, valores, "ValorDeducoes", FormatDecimal(svc.ValorDeducoes.Value));
        AppendElement(doc, valores, "ValorIss", FormatDecimal(svc.ValorIss));
        AppendElement(doc, valores, "Aliquota", FormatDecimal(svc.AliquotaIss));
        AppendElement(doc, valores, "BaseCalculo", FormatDecimal(svc.BaseCalculoIss));
        AppendElement(doc, valores, "IssRetido", svc.IssRetido ? "1" : "2");

        AppendElement(doc, servico, "ItemListaServico", svc.CodigoServico);
        if (!string.IsNullOrWhiteSpace(svc.CodigoTributacaoMunicipio))
            AppendElement(doc, servico, "CodigoTributacaoMunicipio", svc.CodigoTributacaoMunicipio);
        AppendElement(doc, servico, "Discriminacao", svc.Discriminacao);
        AppendElement(doc, servico, "CodigoMunicipio", tenant.Endereco.CodigoMunicipio.ToString());

        var prestador = doc.CreateElement("Prestador", NfseNs);
        infRps.AppendChild(prestador);
        var prestadorCpfCnpj = doc.CreateElement("CpfCnpj", NfseNs);
        prestador.AppendChild(prestadorCpfCnpj);
        AppendElement(doc, prestadorCpfCnpj, "Cnpj", tenant.Cnpj.Valor);
        AppendElement(doc, prestador, "InscricaoMunicipal", tenant.ConfiguracaoFiscal.InscricaoMunicipal!);

        var tom = documento.Tomador;
        var tomadorEl = doc.CreateElement("Tomador", NfseNs);
        infRps.AppendChild(tomadorEl);

        var tomadorIdentificacao = doc.CreateElement("IdentificacaoTomador", NfseNs);
        tomadorEl.AppendChild(tomadorIdentificacao);
        var tomadorCpfCnpj = doc.CreateElement("CpfCnpj", NfseNs);
        tomadorIdentificacao.AppendChild(tomadorCpfCnpj);
        if (tom.CnpjOuCpf.Length == 14)
            AppendElement(doc, tomadorCpfCnpj, "Cnpj", tom.CnpjOuCpf);
        else
            AppendElement(doc, tomadorCpfCnpj, "Cpf", tom.CnpjOuCpf);
        if (!string.IsNullOrWhiteSpace(tom.InscricaoMunicipal))
            AppendElement(doc, tomadorIdentificacao, "InscricaoMunicipal", tom.InscricaoMunicipal);

        AppendElement(doc, tomadorEl, "RazaoSocial", tom.RazaoSocial);

        var tomadorEndereco = doc.CreateElement("Endereco", NfseNs);
        tomadorEl.AppendChild(tomadorEndereco);
        AppendElement(doc, tomadorEndereco, "Endereco", tom.Logradouro);
        AppendElement(doc, tomadorEndereco, "Numero", tom.Numero);
        if (!string.IsNullOrWhiteSpace(tom.Complemento))
            AppendElement(doc, tomadorEndereco, "Complemento", tom.Complemento);
        AppendElement(doc, tomadorEndereco, "Bairro", tom.Bairro);
        AppendElement(doc, tomadorEndereco, "CodigoMunicipio", tom.CodigoMunicipio);
        AppendElement(doc, tomadorEndereco, "Uf", tom.Uf);
        AppendElement(doc, tomadorEndereco, "Cep", tom.Cep);

        if (!string.IsNullOrWhiteSpace(tom.Email))
        {
            var tomadorContato = doc.CreateElement("Contato", NfseNs);
            tomadorEl.AppendChild(tomadorContato);
            AppendElement(doc, tomadorContato, "Email", tom.Email);
        }

        return Result.Success(doc);
    }

    private static XmlElement AppendElement(XmlDocument doc, XmlElement parent, string name, string value)
    {
        var el = doc.CreateElement(name, NfseNs);
        el.InnerText = value;
        parent.AppendChild(el);
        return el;
    }

    private static string FormatDecimal(decimal value) =>
        value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
}
