using OrderFlow.Application.Dtos.Requests;
using OrderFlow.Application.Dtos.Responses;
using OrderFlow.Application.Exceptions;
using OrderFlow.Application.Interfaces;
using OrderFlow.Domain.Entities;
using OrderFlow.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace OrderFlow.Infrastructure.Services;

public class ProdutoService : IProdutoService
{
    private readonly ApplicationDbContext _context;

    public ProdutoService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ProdutoResponse> CriarAsync(CriarProdutoRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Nome))
        {
            throw new RegraDeNegocioException("O nome do produto é obrigatório.");
        }

        if (CriarProdutoRequest.ValidarPreco(request.Preco) is { } erroPreco)
            throw new RegraDeNegocioException(erroPreco);

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            Nome = request.Nome,
            Preco = request.Preco,
            DataCriacao = DateTime.UtcNow
        };

        _context.Produtos.Add(produto);
        await _context.SaveChangesAsync();

        return MapearParaResponse(produto);
    }

    public async Task<ResultadoPaginadoResponse<ProdutoResponse>> ListarAsync(
        PaginacaoRequest? paginacao = null, CancellationToken cancellationToken = default)
    {
        var (pagina, tamanho, deslocamento) = (paginacao ?? new PaginacaoRequest()).Validar();
        var query = _context.Produtos.AsNoTracking();
        var totalItens = await query.LongCountAsync(cancellationToken);

        var produtos = await query
            .OrderBy(produto => produto.Nome)
            .ThenBy(produto => produto.Id)
            .Skip(deslocamento)
            .Take(tamanho)
            .Select(produto => new ProdutoResponse
            {
                Id = produto.Id,
                Nome = produto.Nome,
                Preco = produto.Preco,
                DataCriacao = produto.DataCriacao
            })
            .ToListAsync(cancellationToken);

        return ResultadoPaginadoResponse<ProdutoResponse>.Criar(produtos, pagina, tamanho, totalItens);
    }

    public async Task<ProdutoResponse> BuscarPorIdAsync(Guid id)
    {
        var produto = await _context.Produtos
            .AsNoTracking()
            .FirstOrDefaultAsync(produto => produto.Id == id);

        if (produto is null)
        {
            throw new EntidadeNaoEncontradaException("Produto não encontrado.");
        }

        return MapearParaResponse(produto);
    }

    private static ProdutoResponse MapearParaResponse(Produto produto)
    {
        return new ProdutoResponse
        {
            Id = produto.Id,
            Nome = produto.Nome,
            Preco = produto.Preco,
            DataCriacao = produto.DataCriacao
        };
    }
}
