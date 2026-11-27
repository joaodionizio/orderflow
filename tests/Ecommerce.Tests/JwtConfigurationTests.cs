using System.Globalization;
using System.Security.Cryptography;
using Ecommerce.Application.Dtos.Requests;
using Ecommerce.Application.Dtos.Responses;
using Ecommerce.Infrastructure.Configuration;
using Ecommerce.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ecommerce.Tests;

public class JwtConfigurationTests : ServiceTestBase
{
    [Theory]
    [InlineData(null, 0)]
    [InlineData('a', 0)]
    [InlineData(' ', 32)]
    [InlineData('a', 31)]
    [InlineData('é', 15)]
    public void Load_RejeitaChaveAusenteEmBrancoOuCurta(char? caractere, int quantidade)
    {
        var chave = caractere is char valor ? new string(valor, quantidade) : null;

        var exception = Assert.Throws<InvalidOperationException>(
            () => JwtConfiguration.Load(CriarConfiguracao(chave)));

        Assert.Contains("Jwt__Chave", exception.Message);
        Assert.Contains("32 bytes em UTF-8", exception.Message);
        if (!string.IsNullOrWhiteSpace(chave))
            Assert.DoesNotContain(chave, exception.Message);
    }

    [Theory]
    [InlineData('a', 32, 32)]
    [InlineData('a', 33, 33)]
    [InlineData('é', 16, 32)]
    [InlineData('é', 17, 34)]
    public void Load_ValidaOTamanhoDaChaveEmBytesUtf8(char caractere, int quantidade, int bytes)
    {
        var jwt = JwtConfiguration.Load(CriarConfiguracao(new string(caractere, quantidade)));

        Assert.Equal(bytes * 8, jwt.SigningCredentials.Key.KeySize);
    }

    [Theory]
    [InlineData("Issuer", null)]
    [InlineData("Issuer", "")]
    [InlineData("Issuer", " \t")]
    [InlineData("Audience", null)]
    [InlineData("Audience", "")]
    [InlineData("Audience", " \t")]
    public void Load_RejeitaIssuerEAudienceAusentesOuEmBranco(string campo, string? valor)
    {
        var configuracao = CriarConfiguracao(chave: new string('a', 32), omitirCampo: campo);
        if (valor is not null)
            configuracao[$"Jwt:{campo}"] = valor;

        var exception = Assert.Throws<InvalidOperationException>(() => JwtConfiguration.Load(configuracao));

        Assert.Equal($"Defina Jwt__{campo} com um valor não vazio.", exception.Message);
    }

    [Fact]
    public void Load_UsaSessentaMinutosQuandoExpiracaoEstaAusente()
    {
        var jwt = CriarConfiguracaoJwtValida();

        Assert.Equal(60, jwt.ExpirationMinutes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    [InlineData("configuracao-privada-invalida")]
    [InlineData("1.5")]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("61")]
    [InlineData("2147483648")]
    public void Load_RejeitaExpiracaoInvalidaSemExporOValor(string? valor)
    {
        var configuracao = CriarConfiguracao(chave: new string('a', 32));
        configuracao["Jwt:ExpiraMinutos"] = valor;

        var exception = Assert.Throws<InvalidOperationException>(() => JwtConfiguration.Load(configuracao));

        Assert.Equal("Defina Jwt__ExpiraMinutos como um número inteiro entre 1 e 60.", exception.Message);
    }

    [Theory]
    [InlineData("Issuer", "")]
    [InlineData("Audience", "")]
    [InlineData("Chave", "")]
    [InlineData("ExpiraMinutos", "61")]
    public void Startup_RejeitaJwtInvalidoAntesDeAcessarBanco(string campo, string valor)
    {
        using var factory = new AutorizacaoApiFixture.ApiFactory(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=127.0.0.1,1;Connect Timeout=1",
            ["Jwt:Chave"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            [$"Jwt:{campo}"] = valor
        });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.StartsWith($"Defina Jwt__{campo} ", exception.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(60)]
    public async Task TokenEmitido_UsaDuracaoConfiguradaEPodeSerValidado(int minutos)
    {
        var configuracao = CriarConfiguracao(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        configuracao["Jwt:ExpiraMinutos"] = minutos.ToString(CultureInfo.InvariantCulture);
        var jwt = JwtConfiguration.Load(configuracao);
        var inicio = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var login = await EmitirTokenAsync(jwt);
        var fim = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var parametros = jwt.CreateTokenValidationParameters();

        var resultado = await new JsonWebTokenHandler().ValidateTokenAsync(login.Token, parametros);

        Assert.Equal(minutos, jwt.ExpirationMinutes);
        Assert.Same(jwt.SigningCredentials.Key, parametros.IssuerSigningKey);
        Assert.Equal(TimeSpan.Zero, parametros.ClockSkew);
        Assert.True(resultado.IsValid);
        var emitido = Assert.IsType<JsonWebToken>(resultado.SecurityToken);
        Assert.Equal(SecurityAlgorithms.HmacSha256, emitido.Alg);
        Assert.Equal(emitido.ValidTo, login.ExpiraEm);
        Assert.InRange(new DateTimeOffset(emitido.ValidTo).ToUnixTimeSeconds(), inicio + minutos * 60, fim + minutos * 60);
    }

    [Fact]
    public async Task TokenEmitido_ERejeitadoComOutraChave()
    {
        var login = await EmitirTokenAsync(CriarConfiguracaoJwtValida());
        var parametros = CriarConfiguracaoJwtValida().CreateTokenValidationParameters();

        var resultado = await new JsonWebTokenHandler().ValidateTokenAsync(login.Token, parametros);

        Assert.False(resultado.IsValid);
        Assert.IsType<SecurityTokenSignatureKeyNotFoundException>(resultado.Exception);
    }

    private static IConfiguration CriarConfiguracao(string? chave, string? omitirCampo = null)
    {
        var configuracao = new ConfigurationManager();

        foreach (var campo in new[] { "Issuer", "Audience" })
            if (campo != omitirCampo)
                configuracao[$"Jwt:{campo}"] = "OrderFlow.Tests";

        if (chave is not null)
            configuracao["Jwt:Chave"] = chave;

        return configuracao;
    }

    private static JwtConfiguration CriarConfiguracaoJwtValida() =>
        JwtConfiguration.Load(CriarConfiguracao(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))));

    private static async Task<LoginResponse> EmitirTokenAsync(JwtConfiguration jwt)
    {
        using var contexto = CriarContexto();
        var servico = new AuthService(contexto, jwt);
        const string email = "teste@example.com";
        var senha = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        await servico.RegistrarAsync(new RegistrarUsuarioRequest { Nome = "Teste", Email = email, Senha = senha });

        return await servico.LoginAsync(new LoginRequest { Email = email, Senha = senha });
    }
}
