using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using OrderFlow.Application.Security;

namespace OrderFlow.Application.Dtos.Requests;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class LoginRequest
{
    [Required(ErrorMessage = "O e-mail é obrigatório.")]
    [EmailAddress(ErrorMessage = "O e-mail deve possuir um formato válido.")]
    [MaxLength(EmailDeUsuario.ComprimentoMaximo, ErrorMessage = "O e-mail deve possuir no máximo {1} caracteres.")]
    public string Email { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = true, ErrorMessage = "A senha é obrigatória.")]
    [ValidarSenha(nova: false)]
    public string Senha { get; set; } = string.Empty;
}
