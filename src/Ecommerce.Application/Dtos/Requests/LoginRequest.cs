using System.ComponentModel.DataAnnotations;
using Ecommerce.Application.Security;

namespace Ecommerce.Application.Dtos.Requests;

public class LoginRequest
{
    [Required(ErrorMessage = "O e-mail é obrigatório.")]
    [EmailAddress(ErrorMessage = "O e-mail deve possuir um formato válido.")]
    public string Email { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = true, ErrorMessage = "A senha é obrigatória.")]
    [ValidarSenha(nova: false)]
    public string Senha { get; set; } = string.Empty;
}
