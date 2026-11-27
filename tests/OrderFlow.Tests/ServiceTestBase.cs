using OrderFlow.Domain.Entities;
using OrderFlow.Domain.Enums;
using OrderFlow.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace OrderFlow.Tests;

public abstract class ServiceTestBase
{
    protected static ApplicationDbContext CriarContexto()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    protected static Comprador CriarComprador(ApplicationDbContext context)
    {
        var comprador = new Comprador
        {
            Id = Guid.NewGuid(),
            Nome = "Maria Silva",
            Email = $"maria-{Guid.NewGuid():N}@email.com",
            DataCriacao = DateTime.UtcNow
        };

        context.Compradores.Add(comprador);
        context.SaveChanges();

        return comprador;
    }

    protected static Produto CriarProduto(ApplicationDbContext context, decimal preco = 10)
    {
        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            Nome = "Produto Teste",
            Preco = preco,
            DataCriacao = DateTime.UtcNow
        };

        context.Produtos.Add(produto);
        context.SaveChanges();

        return produto;
    }

    protected static Pedido CriarPedido(
        ApplicationDbContext context,
        StatusPedido status = StatusPedido.Iniciado)
    {
        var comprador = CriarComprador(context);
        var produto = CriarProduto(context);
        var pedido = new Pedido
        {
            Id = Guid.NewGuid(),
            CompradorId = comprador.Id,
            Status = status,
            DataCriacao = DateTime.UtcNow,
            Itens =
            {
                new ItemPedido
                {
                    Id = Guid.NewGuid(),
                    ProdutoId = produto.Id,
                    Quantidade = 1,
                    PrecoUnitario = produto.Preco
                }
            }
        };

        context.Pedidos.Add(pedido);
        context.SaveChanges();

        return pedido;
    }
}
