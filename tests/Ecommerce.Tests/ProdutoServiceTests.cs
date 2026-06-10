using Ecommerce.Application.Dtos.Requests;
using Ecommerce.Application.Exceptions;
using Ecommerce.Infrastructure.Services;

namespace Ecommerce.Tests;

public class ProdutoServiceTests : ServiceTestBase
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CriarAsync_DeveFalhar_QuandoPrecoForMenorOuIgualAZero(decimal preco)
    {
        using var context = CriarContexto();
        var service = new ProdutoService(context);

        var request = new CriarProdutoRequest
        {
            Nome = "Produto Teste",
            Preco = preco
        };

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => service.CriarAsync(request));
    }

    [Fact]
    public async Task CriarAsync_DeveCriarProdutoValido()
    {
        using var context = CriarContexto();
        var service = new ProdutoService(context);

        var request = new CriarProdutoRequest
        {
            Nome = "Teclado",
            Preco = 99.90m
        };

        var produto = await service.CriarAsync(request);

        Assert.NotEqual(Guid.Empty, produto.Id);
        Assert.Equal("Teclado", produto.Nome);
        Assert.Equal(99.90m, produto.Preco);
    }
}
