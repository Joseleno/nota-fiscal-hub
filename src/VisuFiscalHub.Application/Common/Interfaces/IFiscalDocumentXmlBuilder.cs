using System.Xml;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Entities;

namespace VisuFiscalHub.Application.Common.Interfaces;

public interface IFiscalDocumentXmlBuilder
{
    Result<XmlDocument> Construir(DocumentoFiscal documento, Tenant tenant);
}
