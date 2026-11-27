namespace OrderFlow.Domain.Entities;

public class Produto
{
    public Guid Id { get; set; }

    public string Nome { get; set; } = string.Empty;

    public decimal Preco { get; set; }

    public DateTime DataCriacao { get; set; } = DateTime.UtcNow;

    public ICollection<ItemPedido> ItensPedido { get; set; } = new List<ItemPedido>();
}
