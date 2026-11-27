namespace OrderFlow.Application.Dtos.Responses;

public class PedidoResponse
{
    public Guid Id { get; set; }

    public Guid CompradorId { get; set; }

    public string NomeComprador { get; set; } = string.Empty;

    public string EmailComprador { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public decimal ValorTotal { get; set; }

    public DateTime DataCriacao { get; set; }

    public DateTime? DataAtualizacao { get; set; }

    public List<ItemPedidoResponse> Itens { get; set; } = [];
}