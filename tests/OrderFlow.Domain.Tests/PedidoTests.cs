using OrderFlow.Domain.Entities;
using OrderFlow.Domain.Enums;
using Xunit;

namespace OrderFlow.Domain.Tests;

public class PedidoTests
{
    [Fact]
    public void PodeSerAlterado_DeveRetornarTrue_QuandoPedidoEstiverIniciado()
    {
        var pedido = new Pedido { Status = StatusPedido.Iniciado };

        var resultado = pedido.PodeSerAlterado();

        Assert.True(resultado);
    }

    [Fact]
    public void PodeSerAlterado_DeveRetornarFalse_QuandoPedidoEstiverProcessado()
    {
        var pedido = new Pedido { Status = StatusPedido.Processado };

        var resultado = pedido.PodeSerAlterado();

        Assert.False(resultado);
    }

    [Theory]
    [InlineData(StatusPedido.Iniciado, true)]
    [InlineData(StatusPedido.Processado, false)]
    [InlineData(StatusPedido.Enviado, false)]
    [InlineData(StatusPedido.Cancelado, false)]
    public void PodeSerProcessado_DeveRespeitarStatusDoPedido(
        StatusPedido status,
        bool esperado)
    {
        var pedido = new Pedido { Status = status };

        var resultado = pedido.PodeSerProcessado();

        Assert.Equal(esperado, resultado);
    }

    [Theory]
    [InlineData(StatusPedido.Iniciado, true)]
    [InlineData(StatusPedido.Processado, true)]
    [InlineData(StatusPedido.Enviado, false)]
    [InlineData(StatusPedido.Cancelado, false)]
    public void PodeSerCancelado_DeveRespeitarStatusDoPedido(
        StatusPedido status,
        bool esperado)
    {
        var pedido = new Pedido { Status = status };

        var resultado = pedido.PodeSerCancelado();

        Assert.Equal(esperado, resultado);
    }

    [Theory]
    [InlineData(StatusPedido.Processado, true)]
    [InlineData(StatusPedido.Iniciado, false)]
    [InlineData(StatusPedido.Enviado, false)]
    [InlineData(StatusPedido.Cancelado, false)]
    public void PodeSerEnviado_DeveRespeitarStatusDoPedido(
        StatusPedido status,
        bool esperado)
    {
        var pedido = new Pedido { Status = status };

        var resultado = pedido.PodeSerEnviado();

        Assert.Equal(esperado, resultado);
    }

    [Theory]
    [InlineData(StatusPedido.Iniciado, true)]
    [InlineData(StatusPedido.Processado, false)]
    [InlineData(StatusPedido.Enviado, false)]
    [InlineData(StatusPedido.Cancelado, false)]
    public void PodeSerExcluido_DeveRespeitarStatusDoPedido(
        StatusPedido status,
        bool esperado)
    {
        var pedido = new Pedido { Status = status };

        var resultado = pedido.PodeSerExcluido();

        Assert.Equal(esperado, resultado);
    }

    [Fact]
    public void CalcularValorTotal_DeveSomarQuantidadeVezesPrecoUnitario()
    {
        var pedido = new Pedido
        {
            Itens =
            {
                new ItemPedido { Quantidade = 2, PrecoUnitario = 10.50m },
                new ItemPedido { Quantidade = 3, PrecoUnitario = 5.25m }
            }
        };

        var resultado = pedido.CalcularValorTotal();

        Assert.Equal(36.75m, resultado);
    }
}
