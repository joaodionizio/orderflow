namespace OrderFlow.Domain.Entities;

public class ItemPedido
{
    public Guid Id { get; set; }

    public Guid PedidoId { get; set; }

    public Pedido Pedido { get; set; } = null!;

    public Guid ProdutoId { get; set; }

    public Produto Produto { get; set; } = null!;

    public int Quantidade { get; set; }

    public decimal PrecoUnitario { get; set; }

    public decimal CalcularSubtotal()
    {
        // Um preço decimal(18,2) multiplicado por um int cabe nos 96 bits do decimal com centavos.
        return PrecoUnitario * Quantidade;
    }
}
