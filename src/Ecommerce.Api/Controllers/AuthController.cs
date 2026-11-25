using Ecommerce.Api.Authorization;
using Ecommerce.Application.Dtos.Requests;
using Ecommerce.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ecommerce.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("registrar")]
    [Authorize(Policy = PoliticasDeAcesso.Provisionamento)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Registrar(RegistrarUsuarioRequest request)
    {
        await _authService.RegistrarAsync(request);

        return NoContent();
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var response = await _authService.LoginAsync(request);

        return Ok(response);
    }
}
