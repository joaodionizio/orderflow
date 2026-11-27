using Ecommerce.Application.Dtos.Requests;
using Ecommerce.Application.Dtos.Responses;

namespace Ecommerce.Application.Interfaces;

public interface IProdutoService
{
    Task<ProdutoResponse> CriarAsync(CriarProdutoRequest request);

    Task<ResultadoPaginadoResponse<ProdutoResponse>> ListarAsync(
        PaginacaoRequest? paginacao = null, CancellationToken cancellationToken = default);

    Task<ProdutoResponse> BuscarPorIdAsync(Guid id);
}
