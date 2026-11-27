using System.ComponentModel.DataAnnotations;

namespace OrderFlow.Application.Dtos.Requests;

public class ItemPedidoRequest
{
    [Required(ErrorMessage = "O produto é obrigatório.")]
    public Guid ProdutoId { get; set; }

    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "A quantidade do produto deve ser maior que zero.")]
    public int Quantidade { get; set; }
}
