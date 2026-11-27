using Ecommerce.Application.Dtos.Requests;
using Ecommerce.Application.Dtos.Responses;
using Ecommerce.Application.Exceptions;
using Ecommerce.Application.Interfaces;
using Ecommerce.Domain.Entities;
using Ecommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Services;

public class CompradorService : ICompradorService
{
    private readonly ApplicationDbContext _context;

    public CompradorService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<CompradorResponse> CriarAsync(CriarCompradorRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Nome))
        {
            throw new RegraDeNegocioException("O nome do comprador é obrigatório.");
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            throw new RegraDeNegocioException("O e-mail do comprador é obrigatório.");
        }

        var emailJaCadastrado = await _context.Compradores
            .AnyAsync(comprador => comprador.Email == request.Email);

        if (emailJaCadastrado)
        {
            throw new RegraDeNegocioException("Já existe um comprador cadastrado com este e-mail.");
        }

        var comprador = new Comprador
        {
            Id = Guid.NewGuid(),
            Nome = request.Nome,
            Email = request.Email,
            DataCriacao = DateTime.UtcNow
        };

        _context.Compradores.Add(comprador);
        await _context.SaveChangesAsync();

        return MapearParaResponse(comprador);
    }

    public async Task<ResultadoPaginadoResponse<CompradorResponse>> ListarAsync(
        PaginacaoRequest? paginacao = null, CancellationToken cancellationToken = default)
    {
        var (pagina, tamanho, deslocamento) = (paginacao ?? new PaginacaoRequest()).Validar();
        var query = _context.Compradores.AsNoTracking();
        var totalItens = await query.LongCountAsync(cancellationToken);

        var compradores = await query
            .OrderBy(comprador => comprador.Nome)
            .ThenBy(comprador => comprador.Id)
            .Skip(deslocamento)
            .Take(tamanho)
            .Select(comprador => new CompradorResponse
            {
                Id = comprador.Id,
                Nome = comprador.Nome,
                Email = comprador.Email,
                DataCriacao = comprador.DataCriacao
            })
            .ToListAsync(cancellationToken);

        return ResultadoPaginadoResponse<CompradorResponse>.Criar(compradores, pagina, tamanho, totalItens);
    }

    public async Task<CompradorResponse> BuscarPorIdAsync(Guid id)
    {
        var comprador = await _context.Compradores
            .AsNoTracking()
            .FirstOrDefaultAsync(comprador => comprador.Id == id);

        if (comprador is null)
        {
            throw new EntidadeNaoEncontradaException("Comprador não encontrado.");
        }

        return MapearParaResponse(comprador);
    }

    private static CompradorResponse MapearParaResponse(Comprador comprador)
    {
        return new CompradorResponse
        {
            Id = comprador.Id,
            Nome = comprador.Nome,
            Email = comprador.Email,
            DataCriacao = comprador.DataCriacao
        };
    }
}
