using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using OrderFlow.Application.Security;

namespace OrderFlow.Application.Dtos.Requests;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class RegistrarUsuarioRequest
{
    [Required(ErrorMessage = "O nome do usuário é obrigatório.")]
    [MaxLength(150, ErrorMessage = "O nome do usuário deve possuir no máximo 150 caracteres.")]
    public string Nome { get; set; } = string.Empty;

    [Required(ErrorMessage = "O e-mail do usuário é obrigatório.")]
    [EmailAddress(ErrorMessage = "O e-mail do usuário deve possuir um formato válido.")]
    [MaxLength(EmailDeUsuario.ComprimentoMaximo, ErrorMessage = "O e-mail do usuário deve possuir no máximo {1} caracteres.")]
    public string Email { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = true, ErrorMessage = "A senha é obrigatória.")]
    [ValidarSenha(nova: true)]
    public string Senha { get; set; } = string.Empty;
}
