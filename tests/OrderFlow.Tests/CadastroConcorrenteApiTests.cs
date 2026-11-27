using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OrderFlow.Application.Dtos.Requests;
using OrderFlow.Domain.Entities;
using OrderFlow.Domain.Enums;
using OrderFlow.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace OrderFlow.Tests;

public sealed class CadastroConcorrenteApiTests(AutorizacaoApiFixture fixture) : IClassFixture<AutorizacaoApiFixture>
{
    [SqlServerTheory]
    [InlineData("us_english")]
    [InlineData("French")]
    public async Task Cadastro_ResolveDisputaPeloIndiceUnicoSemExporDados(string idioma)
    {
        var conta = AutorizacaoApiTests.NovaConta();
        var outra = AutorizacaoApiTests.NovaConta();
        outra.Email = $"  {conta.Email.ToUpperInvariant()}  ";
        var barreira = new AntesDeBloquearEmail(conta.Email);
        using var logs = new CapturaDeLogs();
        using var api = CriarApi(logs, barreira, idioma);
        using var client = CriarCliente(api);

        var respostas = await Task.WhenAll(RegistrarAsync(client, conta), RegistrarAsync(client, outra));
        using var sucesso = Assert.Single(respostas, resposta => resposta.StatusCode == HttpStatusCode.NoContent);
        using var conflito = Assert.Single(respostas, resposta => resposta.StatusCode == HttpStatusCode.Conflict);
        Assert.True(sucesso.Headers.CacheControl?.NoStore);
        Assert.True(conflito.Headers.CacheControl?.NoStore);
        Assert.Equal(2, barreira.Chegadas);

        var corpo = await conflito.Content.ReadAsStringAsync();
        using var documento = JsonDocument.Parse(corpo);
        Assert.Equal("Já existe um usuário cadastrado com este e-mail.",
            documento.RootElement.GetProperty("mensagem").GetString());
        Assert.Contains(logs.Registros, log => log.Contains("Conflito de unicidade", StringComparison.Ordinal));

        await using var context = fixture.Database.CreateContext();
        var usuario = await context.Usuarios.SingleAsync(u => u.Email == conta.Email);
        Assert.Equal(conta.Email, usuario.Email);
        Assert.Equal(PerfilUsuario.Operator, usuario.Perfil);
        VerificarAusenciaDeDados(corpo, logs, conta.Email, conta.Nome, conta.Senha, outra.Email, outra.Senha, usuario.SenhaHash);

        var duplicidadeSql = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO dbo.Usuarios (Id, Nome, Email, SenhaHash, DataCriacao, Perfil)
            SELECT NEWID(), Nome, Email, SenhaHash, DataCriacao, Perfil FROM dbo.Usuarios WHERE Id = {usuario.Id};
            """));
        Assert.Equal(2601, duplicidadeSql.Number);
        Assert.True(duplicidadeSql.Message.Contains("IX_Usuarios_Email", StringComparison.Ordinal));

        var senhaIncorreta = respostas[0].StatusCode == HttpStatusCode.Conflict ? conta.Senha : outra.Senha;
        var credenciais = new LoginRequest { Email = conta.Email, Senha = senhaIncorreta };
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", credenciais);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        VerificarAusenciaDeDados(await login.Content.ReadAsStringAsync(), logs, conta.Email, senhaIncorreta, usuario.SenhaHash);

        credenciais.Senha = respostas[0].StatusCode == HttpStatusCode.NoContent ? conta.Senha : outra.Senha;
        using var autenticado = await client.PostAsJsonAsync("/api/v1/auth/login", credenciais);
        Assert.Equal(HttpStatusCode.OK, autenticado.StatusCode);
        Assert.True(autenticado.Headers.CacheControl?.NoStore);
        using var resultado = JsonDocument.Parse(await autenticado.Content.ReadAsStringAsync());
        var token = resultado.RootElement.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        VerificarAusenciaDeDados(string.Empty, logs, token, credenciais.Senha, usuario.SenhaHash);
    }

    [SqlServerFact]
    public async Task Cadastro_PreservaErroDeOutroIndiceUnicoSemExporDados()
    {
        const string nomePrivado = "Nome privado para teste IX_Usuarios_Email";
        await using var context = fixture.Database.CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IX_Usuarios_Nome_Teste ON dbo.Usuarios (Nome)
            WHERE Nome = N'Nome privado para teste IX_Usuarios_Email';
            """);
        try
        {
            var primeira = AutorizacaoApiTests.NovaConta();
            var segunda = AutorizacaoApiTests.NovaConta();
            primeira.Nome = segunda.Nome = nomePrivado;
            using var logs = new CapturaDeLogs();
            using var api = CriarApi(logs);
            using var client = CriarCliente(api);
            using var sucesso = await RegistrarAsync(client, primeira);
            using var falha = await RegistrarAsync(client, segunda);

            Assert.Equal(HttpStatusCode.NoContent, sucesso.StatusCode);
            Assert.Equal(HttpStatusCode.InternalServerError, falha.StatusCode);
            Assert.True(falha.Headers.CacheControl?.NoStore);
            var corpo = await falha.Content.ReadAsStringAsync();
            using var documento = JsonDocument.Parse(corpo);
            Assert.Equal("Ocorreu um erro interno no servidor.", documento.RootElement.GetProperty("mensagem").GetString());
            Assert.Contains(logs.Registros, log => log.Contains("SQL: 2601", StringComparison.Ordinal));
            Assert.Contains(logs.Registros, log => log.Contains("Auth.Registrar", StringComparison.Ordinal)
                && log.Contains("DbUpdateException", StringComparison.Ordinal)
                && log.Contains("TraceId: ", StringComparison.Ordinal));
            Assert.DoesNotContain(logs.Registros, log => log.Contains("Conflito de unicidade", StringComparison.Ordinal));
            Assert.False(await context.Usuarios.AnyAsync(u => u.Email == segunda.Email));
            var hash = await context.Usuarios.Where(u => u.Email == primeira.Email).Select(u => u.SenhaHash).SingleAsync();
            VerificarAusenciaDeDados(corpo, logs, nomePrivado, primeira.Email, segunda.Email, primeira.Senha, segunda.Senha, hash);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DROP INDEX IX_Usuarios_Nome_Teste ON dbo.Usuarios;");
        }
    }

    [SqlServerFact]
    public async Task PersistenciaForaDaAutenticacao_IdentificaFalhaSqlSemRegistrarDadosPessoais()
    {
        const string nome = "Nome de comprador privado para teste";
        await using var context = fixture.Database.CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IX_Compradores_Nome_Teste ON dbo.Compradores (Nome)
            WHERE Nome = N'Nome de comprador privado para teste';
            """);
        using var logs = new CapturaDeLogs();
        using var api = CriarApi(logs);
        using var client = CriarCliente(api);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.AdminToken);
        var primeiroEmail = AutorizacaoApiTests.NovaConta().Email;
        var segundoEmail = AutorizacaoApiTests.NovaConta().Email;
        using var sucesso = await client.PostAsJsonAsync("/api/v1/compradores", new { nome, email = primeiroEmail });
        using var falha = await client.PostAsJsonAsync("/api/v1/compradores", new { nome, email = segundoEmail });

        Assert.Equal(HttpStatusCode.Created, sucesso.StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, falha.StatusCode);
        Assert.Contains(logs.Registros, log => log.Contains("Compradores.Criar", StringComparison.Ordinal)
            && log.Contains("SQL: 2601", StringComparison.Ordinal)
            && log.Contains("TraceId: ", StringComparison.Ordinal));
        VerificarAusenciaDeDados(await falha.Content.ReadAsStringAsync(), logs, nome, primeiroEmail, segundoEmail);
    }

    [SqlServerFact]
    public async Task Cadastro_PreservaErro2627EDesvinculaApenasOUsuarioDaTentativa()
    {
        await using var database = new SqlServerTestDatabase();
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();
        await context.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE dbo.Usuarios ADD CONSTRAINT UQ_Usuarios_Nome_Teste UNIQUE (Nome);
            """);
        var auth = database.CreateAuthService(context);
        var primeira = AutorizacaoApiTests.NovaConta();
        var segunda = AutorizacaoApiTests.NovaConta();
        await auth.RegistrarAsync(primeira);
        var contaOriginal = Assert.Single(context.ChangeTracker.Entries<Usuario>()).Entity;

        var erro = await Assert.ThrowsAsync<DbUpdateException>(() => auth.RegistrarAsync(segunda));
        Assert.Equal(2627, Assert.IsType<SqlException>(erro.InnerException).Number);
        Assert.Equal(contaOriginal, Assert.Single(context.ChangeTracker.Entries<Usuario>()).Entity);
        Assert.False(await context.Usuarios.AnyAsync(u => u.Email == segunda.Email));

        segunda.Nome += " nova";
        await auth.RegistrarAsync(segunda);
        Assert.True(await context.Usuarios.AnyAsync(u => u.Email == segunda.Email));
    }

    private WebApplicationFactory<Program> CriarApi(CapturaDeLogs logs, IInterceptor? interceptor = null, string? idioma = null) =>
        fixture.Api.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging
                .AddProvider(logs)
                .AddFilter<CapturaDeLogs>((_, _) => true));
            if (interceptor is not null)
            {
                builder.ConfigureServices(services =>
                {
                    var opcoes = services.Single(s => s.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
                    services.Remove(opcoes);
                    var conexao = new SqlConnectionStringBuilder(fixture.Database.Configuration["ConnectionStrings:DefaultConnection"])
                    {
                        CurrentLanguage = idioma ?? "us_english"
                    };
                    services.AddScoped(provider => new DbContextOptionsBuilder<ApplicationDbContext>(
                            (DbContextOptions<ApplicationDbContext>)opcoes.ImplementationFactory!(provider))
                        .UseSqlServer(conexao.ConnectionString)
                        .AddInterceptors(interceptor).Options);
                });
            }
        });

    private static HttpClient CriarCliente(WebApplicationFactory<Program> api) =>
        api.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

    private async Task<HttpResponseMessage> RegistrarAsync(HttpClient client, RegistrarUsuarioRequest conta)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/registrar") { Content = JsonContent.Create(conta) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.AdminToken);
        return await client.SendAsync(request);
    }

    private void VerificarAusenciaDeDados(string corpo, CapturaDeLogs logs, params string?[] dados)
    {
        var texto = string.Join('\n', logs.Registros.Prepend(corpo));
        var conexao = new SqlConnectionStringBuilder(fixture.Database.Configuration["ConnectionStrings:DefaultConnection"]);
        var segredos = dados.Concat([fixture.AdminToken, fixture.Database.Configuration["Jwt:Chave"]!,
            conexao.ConnectionString, conexao.Password]);
        foreach (var segredo in segredos)
        {
            if (!string.IsNullOrEmpty(segredo))
                Assert.False(texto.Contains(segredo, StringComparison.OrdinalIgnoreCase), "Dados privados apareceram na resposta ou nos logs.");
        }
    }

    private sealed class AntesDeBloquearEmail(string email) : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _duasRequisicoes = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _chegadas;
        public int Chegadas => Volatile.Read(ref _chegadas);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("UPDLOCK, HOLDLOCK", StringComparison.Ordinal)
                && command.Parameters.Cast<DbParameter>().Any(p => Equals(p.Value, email)))
            {
                if (Interlocked.Increment(ref _chegadas) == 2)
                    _duasRequisicoes.SetResult();

                await _duasRequisicoes.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }

            return result;
        }
    }

    private sealed class CapturaDeLogs : ILoggerProvider, ILogger
    {
        public ConcurrentQueue<string> Registros { get; } = new();

        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Registros.Enqueue($"{formatter(state, exception)} {exception}");

        public void Dispose() { }
    }
}
