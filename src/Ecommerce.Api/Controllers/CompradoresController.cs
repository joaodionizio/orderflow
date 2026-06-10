using Ecommerce.Application.Dtos.Requests;
using Ecommerce.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ecommerce.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/compradores")]
public class CompradoresController : ControllerBase
{
    private readonly ICompradorService _compradorService;

    public CompradoresController(ICompradorService compradorService)
    {
        _compradorService = compradorService;
    }

    [HttpPost]
    public async Task<IActionResult> Criar(CriarCompradorRequest request)
    {
        var comprador = await _compradorService.CriarAsync(request);

        return CreatedAtAction(
            nameof(BuscarPorId),
            new { id = comprador.Id },
            comprador);
    }

    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        var compradores = await _compradorService.ListarAsync();

        return Ok(compradores);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> BuscarPorId(Guid id)
    {
        var comprador = await _compradorService.BuscarPorIdAsync(id);

        return Ok(comprador);
    }
}
