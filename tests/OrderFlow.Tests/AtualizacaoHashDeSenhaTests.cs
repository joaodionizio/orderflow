using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using OrderFlow.Application.Dtos.Requests;
using OrderFlow.Application.Exceptions;
using OrderFlow.Domain.Entities;
using OrderFlow.Domain.Enums;
using OrderFlow.Infrastructure.Data;
using OrderFlow.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace OrderFlow.Tests;

public sealed class AtualizacaoHashDeSenhaTests
{
    [SqlServerFact]
    public async Task Login_AtualizaHashLegadoSomenteComSenhaCorretaEPreservaUsuario()
    {
        await using var database = new SqlServerTestDatabase();
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();
        var conta = AutorizacaoApiTests.NovaConta();
        conta.Senha = conta.Senha[..6];
        var legado = HashDeSenhaTests.GerarHashDeTeste(conta.Senha, 100_000);
        var anterior = await GravarUsuarioAsync(context, conta, legado);
        var auth = database.CreateAuthService(context);

        await Assert.ThrowsAsync<CredenciaisInvalidasException>(() => auth.LoginAsync(Credenciais(conta, conta.Senha + "x")));
        Assert.Equal(legado, (await context.Usuarios.AsNoTracking().SingleAsync()).SenhaHash);

        var login = await auth.LoginAsync(Credenciais(conta));
        var atual = await context.Usuarios.AsNoTracking().SingleAsync();
        Assert.Equal(anterior.Id, atual.Id);
        Assert.Equal(anterior.Nome, atual.Nome);
        Assert.Equal(anterior.Email, atual.Email);
        Assert.Equal(anterior.Perfil, atual.Perfil);
        Assert.Equal(anterior.DataCriacao, atual.DataCriacao);
        Assert.StartsWith("600000.", atual.SenhaHash);
        Assert.NotEqual(legado.Split('.')[1], atual.SenhaHash.Split('.')[1]);
        Assert.Equal(ResultadoHashDeSenha.Valido, HashDeSenha.Verificar(conta.Senha, atual.SenhaHash));
        var token = new JwtSecurityTokenHandler().ReadJwtToken(login.Token);
        Assert.Equal(anterior.Id.ToString(), token.Subject);
        Assert.Equal("Operator", Assert.Single(token.Claims, c => c.Type == "role").Value);
    }

    [SqlServerFact]
    public async Task Login_AceitaSenhaLegadaNoTetoEPreservaHashComCustoSuperior()
    {
        await using var database = new SqlServerTestDatabase();
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();
        var conta = AutorizacaoApiTests.NovaConta();
        conta.Senha = PoliticaDeSenhasTests.Frase(1024);
        var legado = HashDeSenhaTests.GerarHashDeTeste(conta.Senha, 100_000);
        var usuario = await GravarUsuarioAsync(context, conta, legado);
        var auth = database.CreateAuthService(context);

        Assert.NotEmpty((await auth.LoginAsync(Credenciais(conta))).Token);
        Assert.StartsWith("600000.", (await context.Usuarios.AsNoTracking().SingleAsync()).SenhaHash);

        var superior = HashDeSenhaTests.GerarHashDeTeste(conta.Senha, 1_200_000);
        usuario.SenhaHash = superior;
        await context.SaveChangesAsync();

        Assert.NotEmpty((await auth.LoginAsync(Credenciais(conta))).Token);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => auth.LoginAsync(Credenciais(conta, conta.Senha + "x")));
        await Assert.ThrowsAsync<CredenciaisInvalidasException>(() => auth.LoginAsync(Credenciais(conta, conta.Senha.Trim())));
        Assert.Equal(superior, (await context.Usuarios.AsNoTracking().SingleAsync()).SenhaHash);
    }

    [SqlServerFact]
    public async Task Login_NaoSobrescreveSenhaAlteradaDuranteAtualizacao()
    {
        await using var database = new SqlServerTestDatabase();
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();
        var conta = AutorizacaoApiTests.NovaConta();
        var legado = HashDeSenhaTests.GerarHashDeTeste(conta.Senha, 100_000);
        var alterado = HashDeSenha.Gerar(AutorizacaoApiTests.NovaConta().Senha);
        var usuario = await GravarUsuarioAsync(context, conta, legado);
        var interferencia = new AntesDeAtualizarHash(async () =>
        {
            usuario.SenhaHash = alterado;
            await context.SaveChangesAsync();
        });
        await using var loginContext = database.CreateContext(interferencia);

        var erro = await Assert.ThrowsAsync<CredenciaisInvalidasException>(
            () => database.CreateAuthService(loginContext).LoginAsync(Credenciais(conta)));

        Assert.Equal("E-mail ou senha inválidos.", erro.Message);
        Assert.True(interferencia.Executado);
        Assert.Equal(alterado, (await context.Usuarios.AsNoTracking().SingleAsync()).SenhaHash);
    }

    [SqlServerFact]
    public async Task Login_AceitaMesmaSenhaQuandoOutraExecucaoAtualizaPrimeiro()
    {
        await using var database = new SqlServerTestDatabase();
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();
        var conta = AutorizacaoApiTests.NovaConta();
        var legado = HashDeSenhaTests.GerarHashDeTeste(conta.Senha, 100_000);
        await GravarUsuarioAsync(context, conta, legado);
        string? primeiroHash = null;
        var interferencia = new AntesDeAtualizarHash(async () =>
        {
            await database.CreateAuthService(context).LoginAsync(Credenciais(conta));
            primeiroHash = (await context.Usuarios.AsNoTracking().SingleAsync()).SenhaHash;
        });
        await using var loginContext = database.CreateContext(interferencia);

        Assert.NotEmpty((await database.CreateAuthService(loginContext).LoginAsync(Credenciais(conta))).Token);
        Assert.NotNull(primeiroHash);
        Assert.StartsWith("600000.", primeiroHash);
        Assert.Equal(primeiroHash, (await context.Usuarios.AsNoTracking().SingleAsync()).SenhaHash);
    }

    private static LoginRequest Credenciais(RegistrarUsuarioRequest conta, string? senha = null) =>
        new() { Email = conta.Email, Senha = senha ?? conta.Senha };

    private static async Task<Usuario> GravarUsuarioAsync(ApplicationDbContext context, RegistrarUsuarioRequest conta, string hash)
    {
        var usuario = new Usuario
        {
            Id = Guid.NewGuid(), Nome = conta.Nome, Email = conta.Email, Perfil = PerfilUsuario.Operator, SenhaHash = hash
        };
        context.Usuarios.Add(usuario);
        await context.SaveChangesAsync();
        return usuario;
    }

    private sealed class AntesDeAtualizarHash(Func<Task> acao) : DbCommandInterceptor
    {
        public bool Executado { get; private set; }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Executado && command.CommandText.StartsWith("UPDATE", StringComparison.Ordinal))
            {
                Executado = true;
                await acao();
            }

            return result;
        }
    }
}
