using Ecommerce.Application.Dtos.Requests;
using Ecommerce.Application.Dtos.Responses;

namespace Ecommerce.Application.Interfaces;

public interface ICompradorService
{
    Task<CompradorResponse> CriarAsync(CriarCompradorRequest request);

    Task<ResultadoPaginadoResponse<CompradorResponse>> ListarAsync(
        PaginacaoRequest? paginacao = null, CancellationToken cancellationToken = default);

    Task<CompradorResponse> BuscarPorIdAsync(Guid id);
}
