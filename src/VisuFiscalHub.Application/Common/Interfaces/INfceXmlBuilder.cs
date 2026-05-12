using System.Xml;
using VisuFiscalHub.Domain.Entities;

namespace VisuFiscalHub.Application.Common.Interfaces;

public interface INfceXmlBuilder
{
    XmlDocument Construir(DocumentoFiscal documento, Tenant tenant);
}
