using OrderFlow.Application.Dtos.Requests;
using OrderFlow.Application.Dtos.Responses;

namespace OrderFlow.Application.Interfaces;

public interface IPedidoService
{
    Task<PedidoResponse> CriarAsync(CriarPedidoRequest request);

    Task<ResultadoPaginadoResponse<PedidoResponse>> ListarAsync(
        PedidoFiltroRequest? filtro, CancellationToken cancellationToken = default);

    Task<PedidoResponse> BuscarPorIdAsync(Guid id);

    Task AtualizarAsync(Guid id, AtualizarPedidoRequest request);

    Task ProcessarAsync(Guid id);

    Task EnviarAsync(Guid id);

    Task CancelarAsync(Guid id);

    Task ExcluirAsync(Guid id);
}
