using OrderFlow.Api.Authorization;
using OrderFlow.Api.Middlewares;
using OrderFlow.Api.RateLimiting;
using OrderFlow.Api.Responses;
using OrderFlow.Application.Dtos.Requests;
using OrderFlow.Application.Dtos.Responses;
using OrderFlow.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace OrderFlow.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    internal const string DesafioLogin = "OrderFlowLogin";

    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("registrar")]
    [Authorize(Policy = PoliticasDeAcesso.Provisionamento)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<MensagemDeErroResponse>(StatusCodes.Status409Conflict, "application/json")]
    public async Task<IActionResult> Registrar(RegistrarUsuarioRequest request)
    {
        await _authService.RegistrarAsync(request);

        return NoContent();
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [RequestSizeLimit(LimitesDeCorpo.Login)]
    [EnableRateLimiting(LimitesHttp.Login)]
    [ServiceFilter(typeof(LimiteDeIdentificadorLoginFilter))]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<MensagemDeErroResponse>(StatusCodes.Status401Unauthorized, "application/json")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var response = await _authService.LoginAsync(request);

        return Ok(response);
    }
}
