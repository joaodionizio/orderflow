using System.Buffers;
using System.Text;

namespace Ecommerce.Application.Security;

public static class PoliticaDeSenhas
{
    private const int MinimoNova = 15;
    private const int MaximoNova = 128;
    private const int MaximoLogin = 1024;

    private static readonly HashSet<string> FrasesComuns = new(StringComparer.OrdinalIgnoreCase)
    {
        "password123456789", "senha1234567890", "orderflow123456", "administrador123",
        "esta é minha senha", "this is my password", "correct horse battery staple"
    };

    private static readonly string[] PadroesComuns =
    [
        "1234567890", "0123456789", "9876543210", "0987654321", "abc", "abcd", "qwerty",
        "qwertyuiop", "asdfghjkl", "zxcvbnm", "password", "senha", "admin", "orderflow"
    ];

    public static string? ValidarNova(string? senha)
    {
        var erro = ValidarComprimento(senha, MinimoNova, MaximoNova);
        if (erro is not null)
            return erro;

        var valor = senha!;
        var primeiroCaractere = valor.AsSpan(0, Rune.GetRuneAt(valor, 0).Utf16SequenceLength);
        if (FrasesComuns.Contains(valor) || CorrespondeAoPadrao(valor, primeiroCaractere)
            || PadroesComuns.Any(padrao => CorrespondeAoPadrao(valor, padrao)))
            return "Escolha uma senha diferente. A senha informada é comum ou previsível.";

        return null;
    }

    public static string? ValidarLogin(string? senha) => ValidarComprimento(senha, minimo: 1, maximo: MaximoLogin);

    private static string? ValidarComprimento(string? senha, int minimo, int maximo)
    {
        if (string.IsNullOrEmpty(senha))
            return "A senha é obrigatória.";

        if (senha.Length > 2 * maximo)
            return $"A senha deve possuir no máximo {maximo} caracteres.";

        var quantidade = 0;
        var restante = senha.AsSpan();
        while (!restante.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(restante, out _, out var unidades) != OperationStatus.Done)
                return "A senha contém caracteres Unicode inválidos.";

            if (++quantidade > maximo)
                return $"A senha deve possuir no máximo {maximo} caracteres.";

            restante = restante[unidades..];
        }

        if (string.IsNullOrWhiteSpace(senha))
            return "A senha é obrigatória.";

        return quantidade < minimo ? $"A senha deve possuir pelo menos {minimo} caracteres." : null;
    }

    private static bool CorrespondeAoPadrao(string senha, ReadOnlySpan<char> padrao)
    {
        var restante = senha.AsSpan();
        while (!restante.IsEmpty)
        {
            var tamanho = Math.Min(restante.Length, padrao.Length);
            if (!restante[..tamanho].Equals(padrao[..tamanho], StringComparison.OrdinalIgnoreCase))
                return false;

            restante = restante[tamanho..];
        }

        return true;
    }
}
