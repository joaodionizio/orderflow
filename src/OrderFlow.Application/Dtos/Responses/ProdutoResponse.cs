namespace OrderFlow.Application.Dtos.Responses;

public class ProdutoResponse
{
    public Guid Id { get; set; }

    public string Nome { get; set; } = string.Empty;

    public decimal Preco { get; set; }

    public DateTime DataCriacao { get; set; }
}