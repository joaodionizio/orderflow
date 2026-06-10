using Ecommerce.Application.Dtos.Requests;
using Ecommerce.Application.Dtos.Responses;

namespace Ecommerce.Application.Interfaces;

public interface IAuthService
{
    Task RegistrarAsync(RegistrarUsuarioRequest request);

    Task<LoginResponse> LoginAsync(LoginRequest request);
}
