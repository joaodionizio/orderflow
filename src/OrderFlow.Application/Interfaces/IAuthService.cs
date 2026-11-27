using OrderFlow.Application.Dtos.Requests;
using OrderFlow.Application.Dtos.Responses;

namespace OrderFlow.Application.Interfaces;

public interface IAuthService
{
    Task RegistrarAsync(RegistrarUsuarioRequest request);

    Task<LoginResponse> LoginAsync(LoginRequest request);
}
