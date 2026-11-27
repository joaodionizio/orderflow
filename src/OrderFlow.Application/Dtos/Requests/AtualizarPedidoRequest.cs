using System.ComponentModel.DataAnnotations;

namespace OrderFlow.Application.Dtos.Requests;

public class AtualizarPedidoRequest
{
    [Required(ErrorMessage = "Os itens do pedido são obrigatórios.")]
    [MinLength(1, ErrorMessage = "O pedido deve possuir pelo menos um item.")]
    [MaxLength(CriarPedidoRequest.MaximoItens, ErrorMessage = "O pedido deve possuir no máximo {1} itens.")]
    public List<ItemPedidoRequest> Itens { get; set; } = [];
}
