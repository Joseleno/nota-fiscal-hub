namespace VisuFiscalHub.Application.Common.Models;

public sealed record EnderecoDto(
    string Logradouro,
    string Numero,
    string? Complemento,
    string Bairro,
    string Municipio,
    int CodigoMunicipio,
    string Uf,
    string Cep,
    string CodigoPais = "1058",
    string? Telefone = null);
