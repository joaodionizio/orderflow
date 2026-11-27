using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using OrderFlow.Infrastructure.Security;

namespace OrderFlow.Tests;

public sealed class HashDeSenhaTests
{
    internal const string EntradaDoVetorDeReferencia = "OrderFlow public PBKDF2 reference";

    [Fact]
    public void Gerar_UsaSaltAleatorioEValidaASenhaIntegral()
    {
        var senha = PoliticaDeSenhasTests.Frase(24);
        var primeiro = HashDeSenha.Gerar(senha);
        var segundo = HashDeSenha.Gerar(senha);

        var campos = primeiro.Split('.');
        Assert.Equal("600000", campos[0]);
        var salt = Convert.FromBase64String(campos[1]);
        var derivado = Convert.FromBase64String(campos[2]);
        Assert.Equal(16, salt.Length);
        Assert.Equal(32, derivado.Length);
        Assert.Equal(Rfc2898DeriveBytes.Pbkdf2(senha, salt, 600_000, HashAlgorithmName.SHA256, 32), derivado);
        Assert.NotEqual(campos[1], segundo.Split('.')[1]);
        Assert.Equal(ResultadoHashDeSenha.Valido, HashDeSenha.Verificar(senha, primeiro));
        Assert.Equal(ResultadoHashDeSenha.SenhaIncorreta, HashDeSenha.Verificar(senha.Trim(), primeiro));
        Assert.Equal(ResultadoHashDeSenha.SenhaIncorreta, HashDeSenha.Verificar(senha.Normalize(NormalizationForm.FormD), primeiro));
    }

    [Fact]
    public void Verificar_AceitaHashLegadoEIndicaAtualizacao()
    {
        var senha = Guid.NewGuid().ToString("N")[..6];
        var registro = GerarHashDeTeste(senha, 100_000);

        Assert.Equal(ResultadoHashDeSenha.AtualizacaoNecessaria, HashDeSenha.Verificar(senha, registro));
        Assert.Equal(ResultadoHashDeSenha.SenhaIncorreta, HashDeSenha.Verificar(senha + "x", registro));
    }

    [Fact]
    public void VerificarParaLogin_ExecutaOCustoPadraoQuandoRegistroEstaAusenteOuCorrompido()
    {
        Assert.Equal(ResultadoHashDeSenha.Valido,
            HashDeSenha.Verificar(EntradaDoVetorDeReferencia, HashDeSenha.RegistroDeReferencia));

        foreach (var registro in new[] { null, "corrompido" })
        {
            var resultado = HashDeSenha.VerificarParaLogin(EntradaDoVetorDeReferencia, registro, out var iteracoes);

            Assert.Equal(ResultadoHashDeSenha.RegistroInvalido, resultado);
            Assert.Equal(600_000, iteracoes);
        }
    }

    [Theory]
    [InlineData(100_000, 600_000)]
    [InlineData(599_999, 600_000)]
    [InlineData(600_000, 600_000)]
    [InlineData(1_200_000, 1_200_000)]
    public void VerificarParaLogin_ExecutaPeloMenosOCustoPadraoQuandoASenhaDiverge(int custo, int custoEsperado)
    {
        var registro = GerarHashDeTeste("senha original de teste", custo);

        var resultado = HashDeSenha.VerificarParaLogin("senha incorreta", registro, out var iteracoes);

        Assert.Equal(ResultadoHashDeSenha.SenhaIncorreta, resultado);
        Assert.Equal(custoEsperado, iteracoes);
    }

    [Theory]
    [InlineData(-100000)]
    [InlineData(0)]
    [InlineData(99999)]
    [InlineData(1200001)]
    [InlineData(int.MaxValue)]
    public void Verificar_RejeitaIteracoesForaDoIntervalo(int iteracoes)
    {
        var salt = Convert.ToBase64String(new byte[16]);
        var hash = Convert.ToBase64String(new byte[32]);

        Assert.Equal(ResultadoHashDeSenha.RegistroInvalido, HashDeSenha.Verificar("teste", $"{iteracoes}.{salt}.{hash}"));
    }

    [Fact]
    public void Verificar_RejeitaEstruturaBase64ETamanhosInvalidos()
    {
        var salt = Convert.ToBase64String(new byte[16]);
        var hash = Convert.ToBase64String(new byte[32]);
        string?[] registros = [null, "", new string('0', 500), $"600000.{salt}", $"600000.{salt}.", $"600000..{hash}",
            $"0600000.{salt}.{hash}", $"600000\0.{salt}.{hash}",
            $"600000.{salt}.{hash}.extra", $"600000.!{salt[1..]}.{hash}", $"600000.{salt}.!{hash[1..]}",
            $"600000.{Convert.ToBase64String(new byte[17])}.{hash}",
            $"600000.{salt}.{Convert.ToBase64String(new byte[31])}",
            $"600000.{salt}.{Convert.ToBase64String(new byte[33])}"];

        foreach (var registro in registros)
            Assert.Equal(ResultadoHashDeSenha.RegistroInvalido, HashDeSenha.Verificar("teste", registro));
    }

    internal static string GerarHashDeTeste(string senha, int iteracoes)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(senha, salt, iteracoes, HashAlgorithmName.SHA256, 32);
        return string.Create(CultureInfo.InvariantCulture, $"{iteracoes}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}");
    }
}
