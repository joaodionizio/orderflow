using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Ecommerce.Application.Dtos.Requests;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class RegistrarUsuarioRequest
{
    [Required(ErrorMessage = "O nome do usuário é obrigatório.")]
    [MaxLength(150, ErrorMessage = "O nome do usuário deve possuir no máximo 150 caracteres.")]
    public string Nome { get; set; } = string.Empty;

    [Required(ErrorMessage = "O e-mail do usuário é obrigatório.")]
    [EmailAddress(ErrorMessage = "O e-mail do usuário deve possuir um formato válido.")]
    [MaxLength(200, ErrorMessage = "O e-mail do usuário deve possuir no máximo 200 caracteres.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "A senha é obrigatória.")]
    [MinLength(6, ErrorMessage = "A senha deve possuir pelo menos 6 caracteres.")]
    public string Senha { get; set; } = string.Empty;
}
