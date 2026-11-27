using System.Buffers;
using System.Buffers.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrderFlow.Application.Serialization;

internal sealed class ConversorDePrecoJson : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.Number &&
            (reader.TokenType != JsonTokenType.String ||
             !options.NumberHandling.HasFlag(JsonNumberHandling.AllowReadingFromString)))
        {
            throw new JsonException();
        }

        ReadOnlySpan<byte> numero = reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan;
        if (reader.TokenType == JsonTokenType.String && reader.ValueIsEscaped)
        {
            var desescapado = new byte[numero.Length];
            numero = desescapado.AsSpan(0, reader.CopyString(desescapado));
        }

        if (!Utf8Parser.TryParse(numero, out decimal preco, out var lidos) || lidos != numero.Length ||
            !RepresentavelEmCentavos(numero))
        {
            throw new JsonException();
        }

        return preco;
    }

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, options);

    private static bool RepresentavelEmCentavos(ReadOnlySpan<byte> numero)
    {
        // A conversão para decimal pode arredondar dígitos do token recebido.
        var indiceExpoente = numero.IndexOfAny((byte)'e', (byte)'E');
        var mantissa = indiceExpoente < 0 ? numero : numero[..indiceExpoente];
        var ultimoDigitoSignificativo = mantissa.LastIndexOfAnyInRange((byte)'1', (byte)'9');
        if (ultimoDigitoSignificativo < 0)
            return true;

        var expoente = 0;
        if (indiceExpoente >= 0 &&
            !Utf8Parser.TryParse(numero[(indiceExpoente + 1)..], out expoente, out _))
        {
            return false;
        }

        var ponto = mantissa.IndexOf((byte)'.');
        var fimDaParteInteira = ponto < 0 ? mantissa.Length : ponto;
        var casasSignificativas = ultimoDigitoSignificativo < fimDaParteInteira
            ? ultimoDigitoSignificativo - fimDaParteInteira + 1
            : ultimoDigitoSignificativo - fimDaParteInteira;
        return (long)casasSignificativas - expoente <= 2;
    }
}
