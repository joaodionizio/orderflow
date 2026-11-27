using Ecommerce.Domain.Enums;

namespace Ecommerce.Application.Dtos.Requests;

public class PedidoFiltroRequest : PaginacaoRequest
{
    public const int TamanhoPaginaPadraoPedidos = 10;

    public PedidoFiltroRequest() : base(TamanhoPaginaPadraoPedidos) { }

    public StatusPedido? Status { get; set; }

    public Guid? CompradorId { get; set; }
}
