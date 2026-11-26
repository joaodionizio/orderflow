using System.Text.Json.Serialization;

namespace Ecommerce.Api.Responses;

internal sealed record MensagemDeErroResponse([property: JsonPropertyName("mensagem")] string Mensagem);
