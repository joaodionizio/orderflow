using System.Globalization;
using System.Security.Cryptography;

namespace Ecommerce.Infrastructure.Security;

public static class HashDeSenha
{
    private const int TamanhoSalt = 16;
    private const int TamanhoHash = 32;
    private const int TamanhoSaltBase64 = 24;
    private const int TamanhoHashBase64 = 44;
    private const int IteracoesMinimas = 100_000;
    private const int IteracoesAtuais = 600_000;
    private const int IteracoesMaximas = 1_200_000;
    private const int TamanhoMinimoRegistro = 6 + 2 + TamanhoSaltBase64 + TamanhoHashBase64;
    private const int TamanhoMaximoRegistro = TamanhoMinimoRegistro + 1;

    // Vetor público preparado para manter o custo do login na ausência de um hash utilizável.
    internal const string RegistroDeReferencia = "600000.NnEdR5h2EMOZRwZNQKlc4A==.a8SqVh6na5fz+z9y4xw+fXs+e08hPW/8CWhvlcLMPyw=";

    public static string Gerar(string senha)
    {
        Span<byte> salt = stackalloc byte[TamanhoSalt];
        Span<byte> hash = stackalloc byte[TamanhoHash];
        RandomNumberGenerator.Fill(salt);
        Rfc2898DeriveBytes.Pbkdf2(senha, salt, hash, IteracoesAtuais, HashAlgorithmName.SHA256);

        return string.Create(CultureInfo.InvariantCulture,
            $"{IteracoesAtuais}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}");
    }

    public static ResultadoHashDeSenha Verificar(string senha, string? registro) =>
        Verificar(senha, registro, completarCustoDeFalha: false, out _);

    internal static ResultadoHashDeSenha VerificarParaLogin(string senha, string? registro, out int iteracoesExecutadas)
    {
        var resultado = Verificar(senha, registro, completarCustoDeFalha: true, out iteracoesExecutadas);
        if (resultado == ResultadoHashDeSenha.RegistroInvalido)
            Verificar(senha, RegistroDeReferencia, completarCustoDeFalha: false, out iteracoesExecutadas);

        return resultado;
    }

    private static ResultadoHashDeSenha Verificar(string senha, string? registro, bool completarCustoDeFalha,
        out int iteracoesExecutadas)
    {
        iteracoesExecutadas = 0;
        if (registro is null || registro.Length is < TamanhoMinimoRegistro or > TamanhoMaximoRegistro)
            return ResultadoHashDeSenha.RegistroInvalido;

        var texto = registro.AsSpan();
        Span<Range> campos = stackalloc Range[3];
        if (texto.Split(campos, '.') != campos.Length)
            return ResultadoHashDeSenha.RegistroInvalido;

        var custo = texto[campos[0]];
        if (custo.IsEmpty || custo[0] == '0' || custo.ContainsAnyExceptInRange('0', '9')
            || !int.TryParse(custo, NumberStyles.None, CultureInfo.InvariantCulture, out var iteracoes)
            || iteracoes is < IteracoesMinimas or > IteracoesMaximas)
            return ResultadoHashDeSenha.RegistroInvalido;

        Span<byte> salt = stackalloc byte[TamanhoSalt];
        Span<byte> esperado = stackalloc byte[TamanhoHash];
        if (!Decodificar(texto[campos[1]], salt, TamanhoSaltBase64)
            || !Decodificar(texto[campos[2]], esperado, TamanhoHashBase64))
            return ResultadoHashDeSenha.RegistroInvalido;

        Span<byte> informado = stackalloc byte[TamanhoHash];
        Rfc2898DeriveBytes.Pbkdf2(senha, salt, informado, iteracoes, HashAlgorithmName.SHA256);
        iteracoesExecutadas = iteracoes;
        if (!CryptographicOperations.FixedTimeEquals(informado, esperado))
        {
            if (completarCustoDeFalha && iteracoes < IteracoesAtuais)
            {
                var iteracoesRestantes = IteracoesAtuais - iteracoes;
                Rfc2898DeriveBytes.Pbkdf2(senha, salt, informado, iteracoesRestantes, HashAlgorithmName.SHA256);
                iteracoesExecutadas += iteracoesRestantes;
            }

            return ResultadoHashDeSenha.SenhaIncorreta;
        }

        return iteracoes < IteracoesAtuais ? ResultadoHashDeSenha.AtualizacaoNecessaria : ResultadoHashDeSenha.Valido;
    }

    private static bool Decodificar(ReadOnlySpan<char> texto, Span<byte> bytes, int tamanhoBase64)
    {
        Span<char> canonico = stackalloc char[TamanhoHashBase64];
        return texto.Length == tamanhoBase64
            && Convert.TryFromBase64Chars(texto, bytes, out var tamanho) && tamanho == bytes.Length
            && Convert.TryToBase64Chars(bytes, canonico, out var escritos) && texto.SequenceEqual(canonico[..escritos]);
    }
}

public enum ResultadoHashDeSenha
{
    RegistroInvalido,
    SenhaIncorreta,
    AtualizacaoNecessaria,
    Valido
}
