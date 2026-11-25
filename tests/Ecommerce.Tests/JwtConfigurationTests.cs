using System.Security.Cryptography;
using Ecommerce.Application.Dtos.Requests;
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

    [Fact]
    public async Task TokenEmitido_PodeSerValidadoComAMesmaConfiguracao()
    {
        var jwt = CriarConfiguracaoJwtValida();
        var token = await EmitirTokenAsync(jwt);
        var parametros = jwt.CreateTokenValidationParameters();

        var resultado = await new JsonWebTokenHandler().ValidateTokenAsync(token, parametros);

        Assert.Same(jwt.SigningCredentials.Key, parametros.IssuerSigningKey);
        Assert.True(resultado.IsValid);
        Assert.Equal(SecurityAlgorithms.HmacSha256, Assert.IsType<JsonWebToken>(resultado.SecurityToken).Alg);
    }

    [Fact]
    public async Task TokenEmitido_ERejeitadoComOutraChave()
    {
        var token = await EmitirTokenAsync(CriarConfiguracaoJwtValida());
        var parametros = CriarConfiguracaoJwtValida().CreateTokenValidationParameters();

        var resultado = await new JsonWebTokenHandler().ValidateTokenAsync(token, parametros);

        Assert.False(resultado.IsValid);
        Assert.IsType<SecurityTokenSignatureKeyNotFoundException>(resultado.Exception);
    }

    private static IConfiguration CriarConfiguracao(string? chave)
    {
        var configuracao = new ConfigurationManager
        {
            ["Jwt:Issuer"] = "OrderFlow.Tests",
            ["Jwt:Audience"] = "OrderFlow.Tests"
        };

        if (chave is not null)
            configuracao["Jwt:Chave"] = chave;

        return configuracao;
    }

    private static JwtConfiguration CriarConfiguracaoJwtValida() =>
        JwtConfiguration.Load(CriarConfiguracao(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))));

    private static async Task<string> EmitirTokenAsync(JwtConfiguration jwt)
    {
        using var contexto = CriarContexto();
        var servico = new AuthService(contexto, jwt);
        const string email = "teste@example.com";
        var senha = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        await servico.RegistrarAsync(new RegistrarUsuarioRequest { Nome = "Teste", Email = email, Senha = senha });

        var login = await servico.LoginAsync(new LoginRequest { Email = email, Senha = senha });
        return login.Token;
    }
}
