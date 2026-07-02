using System.Xml;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Entities;

namespace VisuFiscalHub.Application.Common.Interfaces;

public interface INfseXmlBuilder
{
    Result<XmlDocument> ConstruirRps(DocumentoFiscal documento, Tenant tenant);
}
