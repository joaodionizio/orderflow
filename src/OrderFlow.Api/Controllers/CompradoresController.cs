using OrderFlow.Api.Authorization;
using OrderFlow.Api.Responses;
using OrderFlow.Application.Dtos.Requests;
using OrderFlow.Application.Dtos.Responses;
using OrderFlow.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace OrderFlow.Api.Controllers;

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
    [ProducesResponseType<CompradorResponse>(StatusCodes.Status201Created)]
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
    [ProducesResponseType<CompradorResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<MensagemDeErroResponse>(StatusCodes.Status404NotFound, "application/json")]
    public async Task<IActionResult> BuscarPorId(Guid id)
    {
        var comprador = await _compradorService.BuscarPorIdAsync(id);

        return Ok(comprador);
    }
}
