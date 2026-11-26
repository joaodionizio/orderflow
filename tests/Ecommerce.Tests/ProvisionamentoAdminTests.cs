using System.IdentityModel.Tokens.Jwt;
using Ecommerce.Api.Commands;
using Ecommerce.Application.Dtos.Requests;
using Ecommerce.Application.Exceptions;
using Ecommerce.Domain.Enums;
using Ecommerce.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Ecommerce.Tests;

public sealed class ProvisionamentoAdminTests
{
    [SqlServerFact]
    public async Task Migration_PreservaContaLegadaSemConcederPerfil()
    {
        await using var database = new SqlServerTestDatabase();
        var conta = AutorizacaoApiTests.NovaConta();
        conta.Senha = conta.Senha[..6];
        var id = Guid.NewGuid();
        var criadaEm = DateTime.UtcNow;
        var hashLegado = HashDeSenhaTests.GerarHashDeTeste(conta.Senha, 100_000);

        await using var context = database.CreateContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20260610000000_AdicionarUsuarios");
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dbo.Usuarios (Id, Nome, Email, SenhaHash, DataCriacao)
            VALUES ({id}, {conta.Nome}, {conta.Email}, {hashLegado}, {criadaEm})
            """);

        await migrator.MigrateAsync();

        var usuario = await context.Usuarios.SingleAsync();
        Assert.Equal(id, usuario.Id);
        Assert.Equal(conta.Nome, usuario.Nome);
        Assert.Equal(conta.Email, usuario.Email);
        Assert.Equal(hashLegado, usuario.SenhaHash);
        Assert.Equal(criadaEm, usuario.DataCriacao);
        Assert.Null(usuario.Perfil);

        foreach (var perfilInvalido in new[] { "Outro", "admin", "Admin " })
        {
            var erro = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.Usuarios SET Perfil = {perfilInvalido} WHERE Id = {id}"));
            Assert.Equal(547, erro.Number);
        }

        var auth = database.CreateAuthService(context);
        var credenciais = new LoginRequest { Email = conta.Email, Senha = conta.Senha };
        var anterior = await auth.LoginAsync(credenciais);
        var handler = new JwtSecurityTokenHandler();
        Assert.DoesNotContain(handler.ReadJwtToken(anterior.Token).Claims, claim => claim.Type == "role");

        var duplicada = AutorizacaoApiTests.NovaConta();
        duplicada.Email = conta.Email;
        var erroDuplicidade = await Assert.ThrowsAsync<RegraDeNegocioException>(() => auth.ProvisionarPrimeiroAdminAsync(duplicada));
        Assert.Contains("e-mail", erroDuplicidade.Message);
        await auth.ProvisionarPrimeiroAdminAsync(AutorizacaoApiTests.NovaConta());
        Assert.Equal(1, await context.Usuarios.CountAsync(u => u.Perfil == PerfilUsuario.Admin));

        usuario.Perfil = PerfilUsuario.Operator;
        await context.SaveChangesAsync();
        var atualizado = await auth.LoginAsync(credenciais);
        var token = handler.ReadJwtToken(atualizado.Token);
        Assert.Equal(id.ToString(), token.Subject);
        Assert.Equal("Operator", Assert.Single(token.Claims, claim => claim.Type == "role").Value);
    }

    [SqlServerFact]
    public async Task ProvisionamentoInicial_RejeitaSenhaCurtaSemCriarUsuario()
    {
        await using var database = new SqlServerTestDatabase();
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();
        var auth = database.CreateAuthService(context);
        var conta = AutorizacaoApiTests.NovaConta();
        conta.Senha = conta.Senha[..14];

        var erro = await Assert.ThrowsAsync<RegraDeNegocioException>(() => auth.ProvisionarPrimeiroAdminAsync(conta));

        Assert.Contains("15 caracteres", erro.Message);
        Assert.False(await context.Usuarios.AnyAsync());
    }

    [SqlServerFact]
    public async Task Cli_AplicaPoliticaEOcultaSenhaUnicodeComEspacos()
    {
        await using var database = new SqlServerTestDatabase();
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(database.CreateJwtConfiguration());
        services.AddInfrastructure(database.Configuration["ConnectionStrings:DefaultConnection"]!);
        await using var provider = services.BuildServiceProvider();
        var conta = AutorizacaoApiTests.NovaConta();

        foreach (var senha in new[] { conta.Senha[..14], PoliticaDeSenhasTests.Frase(129),
            PoliticaDeSenhasTests.SenhaComum, PoliticaDeSenhasTests.Frase(15) + '\uD800' })
        {
            Assert.Equal(1, await ExecutarCliAsync(senha));
            Assert.False(await context.Usuarios.AnyAsync());
        }

        var frase = PoliticaDeSenhasTests.Frase(125).Insert(2, "\0\u0001\t");
        Assert.Equal(0, await ExecutarCliAsync(frase, "🚀\b"));
        Assert.Equal(PerfilUsuario.Admin, (await context.Usuarios.SingleAsync()).Perfil);
        var login = await database.CreateAuthService(context).LoginAsync(new LoginRequest { Email = conta.Email, Senha = frase });
        Assert.NotEmpty(login.Token);

        async Task<int> ExecutarCliAsync(string senha, string edicao = "")
        {
            using var entrada = new StringReader($"{conta.Nome}\n{conta.Email}\n");
            using var saida = new StringWriter();
            using var erros = new StringWriter();
            var teclas = new Queue<char>($"{senha}{edicao}\n{senha}\n");
            var codigo = await ProvisionarAdminCommand.ExecutarAsync(provider, entrada, saida, erros, () =>
            {
                var caractere = teclas.Dequeue();
                var tecla = caractere switch
                {
                    '\n' => ConsoleKey.Enter,
                    '\b' => ConsoleKey.Backspace,
                    '\0' => ConsoleKey.D2,
                    '\t' => ConsoleKey.Tab,
                    _ => ConsoleKey.A
                };
                return new ConsoleKeyInfo(caractere, tecla, false, false, caractere is '\0' or '\u0001');
            });

            Assert.DoesNotContain(senha, saida.ToString());
            Assert.DoesNotContain(senha, erros.ToString());
            return codigo;
        }
    }

    [SqlServerFact]
    public async Task ProvisionamentoInicial_CriaSomenteUmAdminMesmoComExecucoesSimultaneas()
    {
        await using var database = new SqlServerTestDatabase();
        await using var verificacao = database.CreateContext();
        await verificacao.Database.MigrateAsync();

        var concorrencia = new ConcorrenciaNoInsert();
        var resultados = await Task.WhenAll(ProvisionarAsync(concorrencia), ProvisionarAsync(concorrencia));

        Assert.True(concorrencia.HouveDeadlock);
        Assert.Single(resultados, provisionado => provisionado);
        Assert.Equal(PerfilUsuario.Admin, (await verificacao.Usuarios.SingleAsync()).Perfil);
        Assert.False(await ProvisionarAsync(concorrencia));

        async Task<bool> ProvisionarAsync(ConcorrenciaNoInsert interceptor)
        {
            await using var context = database.CreateContext(interceptor);
            var auth = database.CreateAuthService(context);
            try
            {
                await auth.ProvisionarPrimeiroAdminAsync(AutorizacaoApiTests.NovaConta());
                return true;
            }
            catch (RegraDeNegocioException ex)
            {
                Assert.Contains("Já existe um administrador", ex.Message);
                Assert.False(context.ChangeTracker.HasChanges());
                return false;
            }
        }
    }

    private sealed class ConcorrenciaNoInsert : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _insercoesProntas = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _tentativas;
        private int _deadlocks;

        public bool HouveDeadlock => Volatile.Read(ref _deadlocks) > 0;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _tentativas) == 2)
                _insercoesProntas.SetResult();

            await _insercoesProntas.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            return result;
        }

        public override Task SaveChangesFailedAsync(
            DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            if (eventData.Exception.GetBaseException() is SqlException { Number: 1205 })
                Interlocked.Increment(ref _deadlocks);

            return Task.CompletedTask;
        }
    }
}
