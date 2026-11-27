namespace OrderFlow.Domain.Entities;

public class Comprador
{
    public Guid Id { get; set; }

    public string Nome { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public DateTime DataCriacao { get; set; } = DateTime.UtcNow;

    public ICollection<Pedido> Pedidos { get; set; } = new List<Pedido>();
}
