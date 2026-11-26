using Ecommerce.Application.Security;

namespace Ecommerce.Tests;

public sealed class PoliticaDeSenhasTests
{
    internal const string SenhaComum = "123456789012345";

    [Theory]
    [InlineData(14, false)]
    [InlineData(15, true)]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public void NovaSenha_MedePontosDeCodigoUnicode(int caracteres, bool aceita)
    {
        Assert.Equal(aceita, PoliticaDeSenhas.ValidarNova(Frase(caracteres)) is null);
    }

    [Theory]
    [InlineData(6, true)]
    [InlineData(129, true)]
    [InlineData(1024, true)]
    [InlineData(1025, false)]
    public void Login_PreservaCompatibilidadeAteOTeto(int caracteres, bool aceita)
    {
        Assert.Equal(aceita, PoliticaDeSenhas.ValidarLogin(Frase(caracteres)) is null);
    }

    [Fact]
    public void NovaSenha_AceitaFrasesComEspacosSemExigirComposicao()
    {
        Assert.Null(PoliticaDeSenhas.ValidarNova("  manhã tranquila no cais  "));
        Assert.Null(PoliticaDeSenhas.ValidarNova($"Uma frase com {SenhaComum} no meio"));
    }

    [Fact]
    public void NovaSenha_RejeitaSenhasPrevisiveisSemAplicarListaAoLogin()
    {
        foreach (var senha in new[] { SenhaComum, "PassWordPassWord", new string('a', 15), string.Concat(Enumerable.Repeat("🌿", 15)) })
        {
            Assert.Contains("previsível", PoliticaDeSenhas.ValidarNova(senha));
            Assert.Null(PoliticaDeSenhas.ValidarLogin(senha));
        }
    }

    [Fact]
    public void Senha_RejeitaUnicodeMalformado()
    {
        foreach (var trecho in new[] { "\uD800", "\uDC00", "\uD800x", "\uDC00\uD800" })
        {
            var senha = Frase(15) + trecho;
            Assert.Contains("Unicode inválidos", PoliticaDeSenhas.ValidarNova(senha));
            Assert.Contains("Unicode inválidos", PoliticaDeSenhas.ValidarLogin(senha));
        }
    }

    internal static string Frase(int caracteres) =>
        "  " + string.Concat(Enumerable.Repeat("🌿", caracteres - 5)) + "é  ";
}
