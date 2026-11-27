using System.ComponentModel.DataAnnotations;

namespace Ecommerce.Application.Dtos.Requests;

public class CriarPedidoRequest
{
    public const int MaximoItens = 100;

    [Required(ErrorMessage = "O comprador é obrigatório.")]
    public Guid CompradorId { get; set; }

    [Required(ErrorMessage = "Os itens do pedido são obrigatórios.")]
    [MinLength(1, ErrorMessage = "O pedido deve possuir pelo menos um item.")]
    [MaxLength(MaximoItens, ErrorMessage = "O pedido deve possuir no máximo {1} itens.")]
    public List<ItemPedidoRequest> Itens { get; set; } = [];
}
