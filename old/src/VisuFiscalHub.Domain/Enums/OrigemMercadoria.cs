namespace VisuFiscalHub.Domain.Enums;

public enum OrigemMercadoria
{
    Nacional = 0,
    EstrangeiraImportacaoDireta = 1,
    EstrangeiraAdquiridaInterna = 2,
    NacionalConteudoImportacaoSuperior40 = 3,
    NacionalProcessosBasicos = 4,
    NacionalConteudoImportacaoInferior40 = 5,
    EstrangeiraImportacaoDiretaSemSimilar = 6,
    EstrangeiraAdquiridaInternaSemSimilar = 7,
    NacionalConteudoImportacao40A70 = 8
}
