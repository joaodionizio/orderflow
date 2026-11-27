using System.ComponentModel.DataAnnotations;

namespace OrderFlow.Application.Dtos.Requests;

public class CriarCompradorRequest
{
    [Required(ErrorMessage = "O nome do comprador é obrigatório.")]
    [MaxLength(150, ErrorMessage = "O nome do comprador deve possuir no máximo 150 caracteres.")]
    public string Nome { get; set; } = string.Empty;

    [Required(ErrorMessage = "O e-mail do comprador é obrigatório.")]
    [EmailAddress(ErrorMessage = "O e-mail do comprador deve possuir um formato válido.")]
    [RegularExpression(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        ErrorMessage = "O e-mail do comprador deve possuir um domínio válido.")]
    [MaxLength(200, ErrorMessage = "O e-mail do comprador deve possuir no máximo 200 caracteres.")]
    public string Email { get; set; } = string.Empty;
}
