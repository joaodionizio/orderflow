using Ecommerce.Application.Dtos.Requests;
using Ecommerce.Application.Dtos.Responses;
using Ecommerce.Application.Exceptions;
using Ecommerce.Application.Interfaces;
using Ecommerce.Domain.Entities;
using Ecommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Services;

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

    public async Task<List<ProdutoResponse>> ListarAsync()
    {
        var produtos = await _context.Produtos
            .AsNoTracking()
            .OrderBy(produto => produto.Nome)
            .ToListAsync();

        return produtos
            .Select(MapearParaResponse)
            .ToList();
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
