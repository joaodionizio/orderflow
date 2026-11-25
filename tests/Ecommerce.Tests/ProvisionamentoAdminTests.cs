using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Ecommerce.Application.Dtos.Requests;
using Ecommerce.Application.Exceptions;
using Ecommerce.Domain.Enums;
using Ecommerce.Infrastructure.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Ecommerce.Tests;

public sealed class ProvisionamentoAdminTests
{
    [SqlServerFact]
    public async Task Migration_PreservaContaLegadaSemConcederPerfil()
    {
        await using var database = new SqlServerTestDatabase();
        var conta = AutorizacaoApiTests.NovaConta();
        var id = Guid.NewGuid();
        var criadaEm = DateTime.UtcNow;
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(conta.Senha, salt, 100_000, HashAlgorithmName.SHA256, 32);
        var hashLegado = $"100000.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";

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

        var auth = new AuthService(context, database.CreateJwtConfiguration());
        var credenciais = new LoginRequest { Email = conta.Email, Senha = conta.Senha };
        var anterior = await auth.LoginAsync(credenciais);
        var handler = new JwtSecurityTokenHandler();
        Assert.DoesNotContain(handler.ReadJwtToken(anterior.Token).Claims, claim => claim.Type == "role");

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => auth.ProvisionarPrimeiroAdminAsync(conta));
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
        var auth = new AuthService(context, database.CreateJwtConfiguration());
        var conta = AutorizacaoApiTests.NovaConta();
        conta.Senha = conta.Senha[..14];

        var erro = await Assert.ThrowsAsync<RegraDeNegocioException>(() => auth.ProvisionarPrimeiroAdminAsync(conta));

        Assert.Contains("15 caracteres", erro.Message);
        Assert.False(await context.Usuarios.AnyAsync());
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
            var auth = new AuthService(context, database.CreateJwtConfiguration());
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
