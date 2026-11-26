using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Ecommerce.Application.Serialization;

namespace Ecommerce.Application.Dtos.Requests;

public class CriarProdutoRequest : IValidatableObject
{
    public const decimal PrecoMinimo = 0.01m;
    public const decimal PrecoMaximo = 9_999_999_999_999_999.99m;

    [Required(ErrorMessage = "O nome do produto é obrigatório.")]
    [MaxLength(150, ErrorMessage = "O nome do produto deve possuir no máximo 150 caracteres.")]
    public string Nome { get; set; } = string.Empty;

    [Required]
    [JsonConverter(typeof(ConversorDePrecoJson))]
    public decimal Preco { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ValidarPreco(Preco) is { } erro)
            yield return new ValidationResult(erro, [nameof(Preco)]);
    }

    public static string? ValidarPreco(decimal preco)
    {
        if (preco < PrecoMinimo)
            return "O preço do produto deve ser de pelo menos 0,01.";

        if (preco > PrecoMaximo)
            return "O preço do produto excede o máximo permitido.";

        return decimal.Round(preco, 2) != preco
            ? "O preço do produto deve possuir no máximo duas casas decimais significativas."
            : null;
    }
}
