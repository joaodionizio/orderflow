using Ecommerce.Application.Dtos.Requests;
using Ecommerce.Application.Dtos.Responses;
using Ecommerce.Application.Exceptions;
using Ecommerce.Application.Interfaces;
using Ecommerce.Domain.Entities;
using Ecommerce.Domain.Enums;
using Ecommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Services;

public class PedidoService : IPedidoService
{
    private readonly ApplicationDbContext _context;

    public PedidoService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PedidoResponse> CriarAsync(CriarPedidoRequest request)
    {
        if (request.CompradorId == Guid.Empty)
            throw new RegraDeNegocioException("O comprador é obrigatório.");

        ValidarItens(request.Itens);

        var comprador = await _context.Compradores
            .FirstOrDefaultAsync(c => c.Id == request.CompradorId);

        if (comprador is null)
            throw new EntidadeNaoEncontradaException("Comprador não encontrado.");

        var pedido = new Pedido
        {
            Id = Guid.NewGuid(),
            CompradorId = comprador.Id,
            Status = StatusPedido.Iniciado,
            DataCriacao = DateTime.UtcNow
        };

        foreach (var itemRequest in request.Itens)
        {
            var produto = await _context.Produtos
                .FirstOrDefaultAsync(p => p.Id == itemRequest.ProdutoId);

            if (produto is null)
                throw new EntidadeNaoEncontradaException("Produto não encontrado.");

            if (produto.Preco <= 0)
                throw new RegraDeNegocioException("O produto deve possuir preço maior que zero.");

            pedido.Itens.Add(new ItemPedido
            {
                Id = Guid.NewGuid(),
                PedidoId = pedido.Id,
                ProdutoId = produto.Id,
                Quantidade = itemRequest.Quantidade,
                PrecoUnitario = produto.Preco
            });
        }

        ValidarTotal(pedido.Itens);
        _context.Pedidos.Add(pedido);
        await _context.SaveChangesAsync();

        return await BuscarPorIdAsync(pedido.Id);
    }

    public async Task<ResultadoPaginadoResponse<PedidoResponse>> ListarAsync(PedidoFiltroRequest filtro)
    {
        filtro ??= new PedidoFiltroRequest();
        var deslocamento = (filtro.Pagina - 1L) * filtro.TamanhoPagina;
        if (deslocamento > int.MaxValue)
            throw new RegraDeNegocioException("O deslocamento da página excede o máximo permitido.");

        var query = _context.Pedidos
            .AsNoTracking()
            .Include(p => p.Comprador)
            .Include(p => p.Itens)
                .ThenInclude(i => i.Produto)
            .AsQueryable();

        if (filtro.Status.HasValue)
            query = query.Where(p => p.Status == filtro.Status.Value);

        if (filtro.CompradorId.HasValue)
            query = query.Where(p => p.CompradorId == filtro.CompradorId.Value);

        var totalItens = await query.CountAsync();

        var pedidos = await query
            .OrderByDescending(p => p.DataCriacao)
            .Skip((int)deslocamento)
            .Take(filtro.TamanhoPagina)
            .ToListAsync();

        return new ResultadoPaginadoResponse<PedidoResponse>
        {
            Items = pedidos.Select(MapearParaResponse).ToList(),
            PaginaAtual = filtro.Pagina,
            TamanhoPagina = filtro.TamanhoPagina,
            TotalItens = totalItens,
            TotalPaginas = (int)Math.Ceiling(totalItens / (double)filtro.TamanhoPagina)
        };
    }

    public async Task<PedidoResponse> BuscarPorIdAsync(Guid id)
    {
        var pedido = await BuscarPedidoCompletoAsync(id, asNoTracking: true);

        if (pedido is null)
            throw new EntidadeNaoEncontradaException("Pedido não encontrado.");

        return MapearParaResponse(pedido);
    }

    public async Task AtualizarAsync(Guid id, AtualizarPedidoRequest request)
    {
        ValidarItens(request.Itens);

        var pedido = await BuscarPedidoCompletoAsync(id);

        if (pedido is null)
            throw new EntidadeNaoEncontradaException("Pedido não encontrado.");

        if (!pedido.PodeSerAlterado())
            throw new RegraDeNegocioException("Apenas pedidos iniciados podem ser alterados.");

        var itensAtuais = pedido.Itens.ToList();
        var novosItens = new List<ItemPedido>();

        foreach (var itemRequest in request.Itens)
        {
            var produto = await _context.Produtos
                .FirstOrDefaultAsync(p => p.Id == itemRequest.ProdutoId);

            if (produto is null)
                throw new EntidadeNaoEncontradaException("Produto não encontrado.");

            if (produto.Preco <= 0)
                throw new RegraDeNegocioException("O produto deve possuir preço maior que zero.");

            novosItens.Add(new ItemPedido
            {
                Id = Guid.NewGuid(),
                PedidoId = pedido.Id,
                ProdutoId = produto.Id,
                Quantidade = itemRequest.Quantidade,
                PrecoUnitario = produto.Preco
            });
        }

        ValidarTotal(novosItens);
        _context.ItensPedido.RemoveRange(itensAtuais);
        await _context.ItensPedido.AddRangeAsync(novosItens);

        pedido.DataAtualizacao = DateTime.UtcNow;

        await _context.SaveChangesAsync();
    }

    private static void ValidarItens(List<ItemPedidoRequest>? itens)
    {
        if (itens is null || itens.Count == 0)
            throw new RegraDeNegocioException("O pedido deve possuir pelo menos um item.");

        if (itens.Count > CriarPedidoRequest.MaximoItens)
            throw new RegraDeNegocioException($"O pedido deve possuir no máximo {CriarPedidoRequest.MaximoItens} itens.");

        foreach (var item in itens)
        {
            if (item is null)
                throw new RegraDeNegocioException("Os itens do pedido não podem ser nulos.");

            if (item.ProdutoId == Guid.Empty)
                throw new RegraDeNegocioException("O produto é obrigatório.");

            if (item.Quantidade <= 0)
                throw new RegraDeNegocioException("A quantidade do produto deve ser maior que zero.");
        }
    }

    private static void ValidarTotal(IEnumerable<ItemPedido> itens)
    {
        if (!Pedido.SomarItens(itens).Exato)
            throw new RegraDeNegocioException("O valor total do pedido excede a precisão monetária suportada.");
    }

    public async Task ProcessarAsync(Guid id)
    {
        var pedido = await BuscarPedidoAsync(id);

        if (pedido is null)
            throw new EntidadeNaoEncontradaException("Pedido não encontrado.");

        if (!pedido.PodeSerProcessado())
            throw new RegraDeNegocioException("Apenas pedidos iniciados podem ser processados.");

        pedido.Status = StatusPedido.Processado;
        pedido.DataAtualizacao = DateTime.UtcNow;

        await _context.SaveChangesAsync();
    }

    public async Task EnviarAsync(Guid id)
    {
        var pedido = await BuscarPedidoAsync(id);

        if (pedido is null)
            throw new EntidadeNaoEncontradaException("Pedido não encontrado.");

        if (!pedido.PodeSerEnviado())
            throw new RegraDeNegocioException("Apenas pedidos processados podem ser enviados.");

        pedido.Status = StatusPedido.Enviado;
        pedido.DataAtualizacao = DateTime.UtcNow;

        await _context.SaveChangesAsync();
    }

    public async Task CancelarAsync(Guid id)
    {
        var pedido = await BuscarPedidoAsync(id);

        if (pedido is null)
            throw new EntidadeNaoEncontradaException("Pedido não encontrado.");

        if (!pedido.PodeSerCancelado())
            throw new RegraDeNegocioException("Apenas pedidos iniciados ou processados podem ser cancelados.");

        pedido.Status = StatusPedido.Cancelado;
        pedido.DataAtualizacao = DateTime.UtcNow;

        await _context.SaveChangesAsync();
    }

    public async Task ExcluirAsync(Guid id)
    {
        var pedido = await BuscarPedidoAsync(id);

        if (pedido is null)
            throw new EntidadeNaoEncontradaException("Pedido não encontrado.");

        if (!pedido.PodeSerExcluido())
            throw new RegraDeNegocioException("Apenas pedidos iniciados podem ser excluídos.");

        _context.Pedidos.Remove(pedido);

        await _context.SaveChangesAsync();
    }

    private async Task<Pedido?> BuscarPedidoAsync(Guid id)
    {
        return await _context.Pedidos
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    private async Task<Pedido?> BuscarPedidoCompletoAsync(Guid id, bool asNoTracking = false)
    {
        var query = _context.Pedidos
            .Include(p => p.Comprador)
            .Include(p => p.Itens)
                .ThenInclude(i => i.Produto)
            .AsQueryable();

        if (asNoTracking)
            query = query.AsNoTracking();

        return await query.FirstOrDefaultAsync(p => p.Id == id);
    }

    private static PedidoResponse MapearParaResponse(Pedido pedido)
    {
        return new PedidoResponse
        {
            Id = pedido.Id,
            CompradorId = pedido.CompradorId,
            NomeComprador = pedido.Comprador.Nome,
            EmailComprador = pedido.Comprador.Email,
            Status = pedido.Status.ToString(),
            ValorTotal = pedido.CalcularValorTotal(),
            DataCriacao = pedido.DataCriacao,
            DataAtualizacao = pedido.DataAtualizacao,
            Itens = pedido.Itens.Select(item => new ItemPedidoResponse
            {
                ProdutoId = item.ProdutoId,
                NomeProduto = item.Produto.Nome,
                Quantidade = item.Quantidade,
                PrecoUnitario = item.PrecoUnitario,
                Subtotal = item.CalcularSubtotal()
            }).ToList()
        };
    }
}
