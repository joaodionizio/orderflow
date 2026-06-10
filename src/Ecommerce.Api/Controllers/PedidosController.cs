using Ecommerce.Application.Dtos.Requests;
using Ecommerce.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ecommerce.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/pedidos")]
public class PedidosController : ControllerBase
{
    private readonly IPedidoService _pedidoService;

    public PedidosController(IPedidoService pedidoService)
    {
        _pedidoService = pedidoService;
    }

    [HttpPost]
    public async Task<IActionResult> Criar(CriarPedidoRequest request)
    {
        var pedido = await _pedidoService.CriarAsync(request);

        return CreatedAtAction(
            nameof(BuscarPorId),
            new { id = pedido.Id },
            pedido);
    }

    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] PedidoFiltroRequest filtro)
    {
        var pedidos = await _pedidoService.ListarAsync(filtro);

        return Ok(pedidos);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> BuscarPorId(Guid id)
    {
        var pedido = await _pedidoService.BuscarPorIdAsync(id);

        return Ok(pedido);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Atualizar(Guid id, AtualizarPedidoRequest request)
    {
        await _pedidoService.AtualizarAsync(id, request);

        return NoContent();
    }

    [HttpPatch("{id:guid}/processar")]
    public async Task<IActionResult> Processar(Guid id)
    {
        await _pedidoService.ProcessarAsync(id);

        return NoContent();
    }

    [HttpPatch("{id:guid}/enviar")]
    public async Task<IActionResult> Enviar(Guid id)
    {
        await _pedidoService.EnviarAsync(id);

        return NoContent();
    }

    [HttpPatch("{id:guid}/cancelar")]
    public async Task<IActionResult> Cancelar(Guid id)
    {
        await _pedidoService.CancelarAsync(id);

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Excluir(Guid id)
    {
        await _pedidoService.ExcluirAsync(id);

        return NoContent();
    }
}
