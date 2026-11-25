using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Ecommerce.Application.Dtos.Requests;
using Ecommerce.Application.Dtos.Responses;
using Ecommerce.Domain.Enums;
using Ecommerce.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Ecommerce.Tests;

public sealed class AutorizacaoApiTests(AutorizacaoApiFixture fixture) : IClassFixture<AutorizacaoApiFixture>
{
    private const string Cadastro = "/api/v1/auth/registrar";
    private static readonly string[] RotasOperacionais = ["/api/v1/compradores", "/api/v1/produtos", "/api/v1/pedidos"];

    [SqlServerFact]
    public async Task Anonimo_NaoPodeCadastrarContas()
    {
        using var response = await EnviarAsync(HttpMethod.Post, Cadastro, token: null, NovaConta());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [SqlServerFact]
    public async Task OperadorAutenticado_NaoPodeCadastrarContas()
    {
        using var response = await EnviarAsync(HttpMethod.Post, Cadastro, fixture.OperatorToken, NovaConta());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [SqlServerFact]
    public async Task Admin_PodeProvisionarOperadorComRolePersistidaEEmitidaNoLogin()
    {
        var novo = NovaConta();
        using var response = await EnviarAsync(HttpMethod.Post, Cadastro, fixture.AdminToken, novo);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var context = fixture.Database.CreateContext();
        var usuario = await context.Usuarios.SingleAsync(usuario => usuario.Email == novo.Email);
        Assert.Equal(PerfilUsuario.Operator, usuario.Perfil);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(await fixture.LoginAsync(novo));
        Assert.Equal("Operator", Assert.Single(token.Claims, claim => claim.Type == "role").Value);
        Assert.Equal(usuario.Id.ToString(), token.Subject);
    }

    [SqlServerFact]
    public async Task Cadastro_RejeitaCamposDePerfilMesmoQuandoSolicitadoPorAdmin()
    {
        await using var context = fixture.Database.CreateContext();
        foreach (var campo in new[] { "role", "perfil" })
        {
            var novo = NovaConta();
            using var response = await EnviarAsync(HttpMethod.Post, Cadastro, fixture.AdminToken,
                new Dictionary<string, string>
                {
                    ["nome"] = novo.Nome,
                    ["email"] = novo.Email,
                    ["senha"] = novo.Senha,
                    [campo] = "Admin"
                });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(await context.Usuarios.AnyAsync(usuario => usuario.Email == novo.Email));
        }
    }

    [SqlServerFact]
    public async Task AdminEOperator_PodemAcessarCadaControllerOperacional()
    {
        foreach (var token in new[] { fixture.AdminToken, fixture.OperatorToken })
        {
            foreach (var rota in RotasOperacionais)
            {
                using var response = await EnviarAsync(HttpMethod.Get, rota, token);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            using var criacao = await EnviarAsync(HttpMethod.Post, "/api/v1/produtos", token,
                new { nome = "Produto de teste", preco = 10 });
            Assert.Equal(HttpStatusCode.Created, criacao.StatusCode);
        }
    }

    [SqlServerFact]
    public async Task TokensValidosSemPerfilComPerfilDesconhecidoOuComVariosPerfis_Recebem403()
    {
        string[][] perfisInvalidos = [[], ["Desconhecido"], ["Admin", "Desconhecido"], ["Admin", "Operator"]];
        foreach (var perfis in perfisInvalidos)
        {
            var token = fixture.CriarTokenDeTeste(perfis);
            foreach (var rota in RotasOperacionais)
            {
                using var response = await EnviarAsync(HttpMethod.Get, rota, token);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }

            using var cadastro = await EnviarAsync(HttpMethod.Post, Cadastro, token, NovaConta());
            Assert.Equal(HttpStatusCode.Forbidden, cadastro.StatusCode);
        }
    }

    [SqlServerFact]
    public async Task TokensAusentesOuInvalidos_Recebem401()
    {
        foreach (var token in new string?[] { null, "token-invalido" })
        {
            foreach (var rota in RotasOperacionais)
            {
                using var response = await EnviarAsync(HttpMethod.Get, rota, token);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }
        }
    }

    [SqlServerFact]
    public async Task Swagger_DocumentaBearerEPerfisSomenteNasRotasProtegidas()
    {
        using var swagger = JsonDocument.Parse(await fixture.Client.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = swagger.RootElement.GetProperty("paths");
        var login = paths.GetProperty("/api/v1/auth/login").GetProperty("post");
        Assert.False(login.TryGetProperty("security", out _));

        var cadastro = paths.GetProperty(Cadastro).GetProperty("post");
        Assert.Equal("Perfil Admin obrigatório.", cadastro.GetProperty("description").GetString());
        Assert.True(cadastro.GetProperty("security")[0].TryGetProperty("Bearer", out _));
        Assert.True(cadastro.GetProperty("responses").TryGetProperty("401", out _));
        Assert.True(cadastro.GetProperty("responses").TryGetProperty("403", out _));

        var pedidos = paths.GetProperty("/api/v1/pedidos").GetProperty("get");
        Assert.Equal("Perfil Admin ou Operator obrigatório.", pedidos.GetProperty("description").GetString());
    }

    internal static RegistrarUsuarioRequest NovaConta() => new()
    {
        Nome = "Conta de teste",
        Email = $"{Guid.NewGuid():N}@example.test",
        Senha = Convert.ToHexString(RandomNumberGenerator.GetBytes(16))
    };

    private async Task<HttpResponseMessage> EnviarAsync(HttpMethod method, string rota, string? token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, rota);
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await fixture.Client.SendAsync(request);
    }
}

public sealed class AutorizacaoApiFixture : IAsyncLifetime
{
    private WebApplicationFactory<Program>? _factory;
    internal SqlServerTestDatabase Database { get; private set; } = null!;
    internal HttpClient Client { get; private set; } = null!;
    internal string AdminToken { get; private set; } = null!;
    internal string OperatorToken { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        if (SqlServerTestDatabase.PodeIgnorarTestes)
            return;

        Database = new SqlServerTestDatabase();
        var admin = AutorizacaoApiTests.NovaConta();
        var operador = AutorizacaoApiTests.NovaConta();

        await using (var context = Database.CreateContext())
        {
            await context.Database.MigrateAsync();
            var auth = new AuthService(context, Database.CreateJwtConfiguration());
            await auth.ProvisionarPrimeiroAdminAsync(admin);
            await auth.RegistrarAsync(operador);
        }

        _factory = new ApiFactory(Database.Configuration);
        Client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        AdminToken = await LoginAsync(admin);
        OperatorToken = await LoginAsync(operador);
    }

    internal async Task<string> LoginAsync(RegistrarUsuarioRequest conta)
    {
        using var response = await Client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Email = conta.Email,
            Senha = conta.Senha
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        return Assert.IsType<LoginResponse>(login).Token;
    }

    internal string CriarTokenDeTeste(IEnumerable<string> perfis)
    {
        var jwt = Database.CreateJwtConfiguration();
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()) };
        claims.AddRange(perfis.Select(perfil => new Claim("role", perfil)));

        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            jwt.Issuer, jwt.Audience, claims, expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: jwt.SigningCredentials));
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        if (Database is not null)
            await Database.DisposeAsync();
    }

    private sealed class ApiFactory(IReadOnlyDictionary<string, string?> configuration) : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(configuration));
            return base.CreateHost(builder);
        }
    }
}
