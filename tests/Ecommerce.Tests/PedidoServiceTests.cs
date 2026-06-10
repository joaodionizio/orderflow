using Ecommerce.Application.Dtos.Requests;
using Ecommerce.Application.Exceptions;
using Ecommerce.Domain.Enums;
using Ecommerce.Infrastructure.Services;

namespace Ecommerce.Tests;

public class PedidoServiceTests : ServiceTestBase
{
    [Fact]
    public async Task CriarAsync_DeveFalhar_QuandoCompradorNaoForInformado()
    {
        using var context = CriarContexto();
        var service = new PedidoService(context);

        var request = new CriarPedidoRequest
        {
            CompradorId = Guid.Empty,
            Itens = [new ItemPedidoRequest { ProdutoId = Guid.NewGuid(), Quantidade = 1 }]
        };

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => service.CriarAsync(request));
    }

    [Fact]
    public async Task CriarAsync_DeveFalhar_QuandoNaoPossuirItens()
    {
        using var context = CriarContexto();
        var comprador = CriarComprador(context);
        var service = new PedidoService(context);

        var request = new CriarPedidoRequest
        {
            CompradorId = comprador.Id,
            Itens = []
        };

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => service.CriarAsync(request));
    }

    [Fact]
    public async Task CriarAsync_DeveFalhar_QuandoProdutoNaoExistir()
    {
        using var context = CriarContexto();
        var comprador = CriarComprador(context);
        var service = new PedidoService(context);

        var request = new CriarPedidoRequest
        {
            CompradorId = comprador.Id,
            Itens = [new ItemPedidoRequest { ProdutoId = Guid.NewGuid(), Quantidade = 1 }]
        };

        await Assert.ThrowsAsync<EntidadeNaoEncontradaException>(() => service.CriarAsync(request));
    }

    [Fact]
    public async Task CriarAsync_DeveFalhar_QuandoQuantidadeForZero()
    {
        using var context = CriarContexto();
        var comprador = CriarComprador(context);
        var produto = CriarProduto(context);
        var service = new PedidoService(context);

        var request = new CriarPedidoRequest
        {
            CompradorId = comprador.Id,
            Itens = [new ItemPedidoRequest { ProdutoId = produto.Id, Quantidade = 0 }]
        };

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => service.CriarAsync(request));
    }

    [Fact]
    public async Task CriarAsync_DeveCriarPedido_ComStatusIniciado()
    {
        using var context = CriarContexto();
        var comprador = CriarComprador(context);
        var produto = CriarProduto(context);
        var service = new PedidoService(context);

        var request = new CriarPedidoRequest
        {
            CompradorId = comprador.Id,
            Itens = [new ItemPedidoRequest { ProdutoId = produto.Id, Quantidade = 2 }]
        };

        var pedido = await service.CriarAsync(request);

        Assert.Equal(StatusPedido.Iniciado.ToString(), pedido.Status);
        Assert.Equal(20, pedido.ValorTotal);
    }

    [Fact]
    public async Task AtualizarAsync_DeveFalhar_QuandoPedidoEstiverProcessado()
    {
        using var context = CriarContexto();
        var pedido = CriarPedido(context, StatusPedido.Processado);
        var produto = CriarProduto(context);
        var service = new PedidoService(context);

        var request = new AtualizarPedidoRequest
        {
            Itens = [new ItemPedidoRequest { ProdutoId = produto.Id, Quantidade = 1 }]
        };

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => service.AtualizarAsync(pedido.Id, request));
    }

    [Fact]
    public async Task CancelarAsync_DeveCancelarPedidoIniciado()
    {
        using var context = CriarContexto();
        var pedido = CriarPedido(context);
        var service = new PedidoService(context);

        await service.CancelarAsync(pedido.Id);

        var pedidoAtualizado = await context.Pedidos.FindAsync(pedido.Id);
        Assert.Equal(StatusPedido.Cancelado, pedidoAtualizado!.Status);
    }

    [Fact]
    public async Task EnviarAsync_DeveFalhar_QuandoPedidoNaoEstiverProcessado()
    {
        using var context = CriarContexto();
        var pedido = CriarPedido(context);
        var service = new PedidoService(context);

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => service.EnviarAsync(pedido.Id));
    }

    [Fact]
    public async Task EnviarAsync_DeveEnviarPedidoProcessado()
    {
        using var context = CriarContexto();
        var pedido = CriarPedido(context, StatusPedido.Processado);
        var service = new PedidoService(context);

        await service.EnviarAsync(pedido.Id);

        var pedidoAtualizado = await context.Pedidos.FindAsync(pedido.Id);
        Assert.Equal(StatusPedido.Enviado, pedidoAtualizado!.Status);
    }
}
