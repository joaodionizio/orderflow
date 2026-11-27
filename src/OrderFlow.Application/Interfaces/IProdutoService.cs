using OrderFlow.Application.Dtos.Requests;
using OrderFlow.Application.Dtos.Responses;

namespace OrderFlow.Application.Interfaces;

public interface IProdutoService
{
    Task<ProdutoResponse> CriarAsync(CriarProdutoRequest request);

    Task<ResultadoPaginadoResponse<ProdutoResponse>> ListarAsync(
        PaginacaoRequest? paginacao = null, CancellationToken cancellationToken = default);

    Task<ProdutoResponse> BuscarPorIdAsync(Guid id);
}
