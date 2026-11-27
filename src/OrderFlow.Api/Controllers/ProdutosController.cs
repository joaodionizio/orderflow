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
[Route("api/v1/produtos")]
public class ProdutosController : ControllerBase
{
    private readonly IProdutoService _produtoService;

    public ProdutosController(IProdutoService produtoService)
    {
        _produtoService = produtoService;
    }

    [HttpPost]
    [ProducesResponseType<ProdutoResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Criar(CriarProdutoRequest request)
    {
        var produto = await _produtoService.CriarAsync(request);

        return CreatedAtAction(
            nameof(BuscarPorId),
            new { id = produto.Id },
            produto);
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ResultadoPaginadoResponse<ProdutoResponse>>> Listar(
        [FromQuery(Name = "")] PaginacaoRequest paginacao, CancellationToken cancellationToken)
    {
        var produtos = await _produtoService.ListarAsync(paginacao, cancellationToken);

        return Ok(produtos);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProdutoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<MensagemDeErroResponse>(StatusCodes.Status404NotFound, "application/json")]
    public async Task<IActionResult> BuscarPorId(Guid id)
    {
        var produto = await _produtoService.BuscarPorIdAsync(id);

        return Ok(produto);
    }
}
