using OrderFlow.Application.Dtos.Requests;
using OrderFlow.Application.Dtos.Responses;

namespace OrderFlow.Application.Interfaces;

public interface ICompradorService
{
    Task<CompradorResponse> CriarAsync(CriarCompradorRequest request);

    Task<ResultadoPaginadoResponse<CompradorResponse>> ListarAsync(
        PaginacaoRequest? paginacao = null, CancellationToken cancellationToken = default);

    Task<CompradorResponse> BuscarPorIdAsync(Guid id);
}
