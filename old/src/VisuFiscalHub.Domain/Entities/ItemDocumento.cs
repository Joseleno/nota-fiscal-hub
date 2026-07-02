using VisuFiscalHub.Domain.ValueObjects;

namespace VisuFiscalHub.Domain.Entities;

public sealed class ItemDocumento
{
    private ItemDocumento()
    {
        // Para EF Core — inicialização via reflexão
        Produto = default!;
        Tributo = default!;
    }

    public ItemDocumento(int numero, Produto produto, Tributo tributo)
    {
        Numero = numero;
        Produto = produto;
        Tributo = tributo;
    }

    public int Numero { get; private set; }
    public Produto Produto { get; private set; }
    public Tributo Tributo { get; private set; }

    public decimal ValorTotal => Produto.ValorLiquido;
}
