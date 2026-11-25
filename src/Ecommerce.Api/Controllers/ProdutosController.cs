using Ecommerce.Api.Authorization;
using Ecommerce.Application.Dtos.Requests;
using Ecommerce.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ecommerce.Api.Controllers;

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
    public async Task<IActionResult> Criar(CriarProdutoRequest request)
    {
        var produto = await _produtoService.CriarAsync(request);

        return CreatedAtAction(
            nameof(BuscarPorId),
            new { id = produto.Id },
            produto);
    }

    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        var produtos = await _produtoService.ListarAsync();

        return Ok(produtos);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> BuscarPorId(Guid id)
    {
        var produto = await _produtoService.BuscarPorIdAsync(id);

        return Ok(produto);
    }
}
