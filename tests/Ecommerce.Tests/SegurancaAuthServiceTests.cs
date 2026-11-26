using System.Security.Cryptography;
using Ecommerce.Application.Dtos.Requests;
using Ecommerce.Application.Exceptions;
using Ecommerce.Domain.Entities;
using Ecommerce.Infrastructure.Configuration;
using Ecommerce.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Ecommerce.Tests;

public sealed class SegurancaAuthServiceTests : ServiceTestBase
{
    [Fact]
    public async Task Registrar_AplicaPoliticaTambemNaChamadaDiretaDoServico()
    {
        await using var context = CriarContexto();
        var service = new AuthService(context, CriarJwt(), new LoggerDeTeste());
        var conta = AutorizacaoApiTests.NovaConta();

        foreach (var senha in new[] { conta.Senha[..14], PoliticaDeSenhasTests.Frase(129), PoliticaDeSenhasTests.SenhaComum })
        {
            conta.Senha = senha;
            await Assert.ThrowsAsync<RegraDeNegocioException>(() => service.RegistrarAsync(conta));
        }

        Assert.False(await context.Usuarios.AnyAsync());
    }

    [Fact]
    public async Task Login_RegistraHashInvalidoSemIdentificadoresNemSegredos()
    {
        await using var context = CriarContexto();
        var conta = AutorizacaoApiTests.NovaConta();
        var usuario = new Usuario { Id = Guid.NewGuid(), Nome = conta.Nome, Email = conta.Email, SenhaHash = "registro-de-teste-invalido" };
        context.Usuarios.Add(usuario);
        await context.SaveChangesAsync();
        var logger = new LoggerDeTeste();
        var service = new AuthService(context, CriarJwt(), logger);

        var erro = await Assert.ThrowsAsync<RegraDeNegocioException>(
            () => service.LoginAsync(new LoginRequest { Email = conta.Email, Senha = conta.Senha }));

        Assert.Equal("E-mail ou senha inválidos.", erro.Message);
        var registro = Assert.Single(logger.Registros);
        Assert.Equal(LogLevel.Warning, registro.Nivel);
        Assert.Equal("Registro de hash de senha inválido encontrado durante a autenticação.", registro.Mensagem);
    }

    private static JwtConfiguration CriarJwt() => JwtConfiguration.Load(new ConfigurationManager
    {
        ["Jwt:Chave"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
    });

    private sealed class LoggerDeTeste : ILogger<AuthService>
    {
        public List<(LogLevel Nivel, string Mensagem)> Registros { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Registros.Add((logLevel, formatter(state, exception)));
    }
}
