using Ecommerce.Api.Authorization;
using Ecommerce.Application.Dtos.Requests;
using Ecommerce.Application.Dtos.Responses;
using Ecommerce.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ecommerce.Api.Controllers;

[ApiController]
[Authorize(Policy = PoliticasDeAcesso.Operacao)]
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
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ResultadoPaginadoResponse<CompradorResponse>>> Listar(
        [FromQuery(Name = "")] PaginacaoRequest paginacao, CancellationToken cancellationToken)
    {
        var compradores = await _compradorService.ListarAsync(paginacao, cancellationToken);

        return Ok(compradores);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> BuscarPorId(Guid id)
    {
        var comprador = await _compradorService.BuscarPorIdAsync(id);

        return Ok(comprador);
    }
}
