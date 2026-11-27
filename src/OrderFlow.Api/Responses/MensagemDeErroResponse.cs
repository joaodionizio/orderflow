using System.Text.Json.Serialization;

namespace OrderFlow.Api.Responses;

internal sealed record MensagemDeErroResponse([property: JsonPropertyName("mensagem")] string Mensagem);
