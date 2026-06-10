using Ecommerce.Application.Dtos.Requests;
using Ecommerce.Application.Exceptions;
using Ecommerce.Infrastructure.Services;

namespace Ecommerce.Tests;

public class CompradorServiceTests : ServiceTestBase
{
    [Fact]
    public async Task CriarAsync_DeveFalhar_QuandoNomeNaoForInformado()
    {
        using var context = CriarContexto();
        var service = new CompradorService(context);

        var request = new CriarCompradorRequest
        {
            Nome = "",
            Email = "comprador@email.com"
        };

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => service.CriarAsync(request));
    }

    [Fact]
    public async Task CriarAsync_DeveFalhar_QuandoEmailNaoForInformado()
    {
        using var context = CriarContexto();
        var service = new CompradorService(context);

        var request = new CriarCompradorRequest
        {
            Nome = "Comprador",
            Email = ""
        };

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => service.CriarAsync(request));
    }

    [Fact]
    public async Task CriarAsync_DeveFalhar_QuandoEmailForDuplicado()
    {
        using var context = CriarContexto();
        var service = new CompradorService(context);
        var request = new CriarCompradorRequest
        {
            Nome = "Comprador",
            Email = "comprador@email.com"
        };

        await service.CriarAsync(request);

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => service.CriarAsync(request));
    }
}
