using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ecommerce.Api.Health;
using Ecommerce.Api.RateLimiting;
using Ecommerce.Application.Dtos.Requests;
using Ecommerce.Application.Dtos.Responses;
using Ecommerce.Application.Interfaces;
using Ecommerce.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ecommerce.Tests;

public class LimitesHttpTests(AutorizacaoApiFixture fixture) : IClassFixture<AutorizacaoApiFixture>
{
    private const string Login = "/api/v1/auth/login";
    private const string Cadastro = "/api/v1/auth/registrar";
    private const string Produtos = "/api/v1/produtos";
    private const string Compradores = "/api/v1/compradores";
    private const string Saude = "/health";
    private const string Origem = "192.0.2.10";
    private const string OutraOrigem = "192.0.2.20";

    [SqlServerFact]
    public async Task Login_UsaOrigemEncaminhadaSomenteDoProxyConfiavel()
    {
        const string host = "orderflow.example.test";
        const string proxy = "::ffff:192.0.2.5";
        const string clienteEncaminhado = "198.51.100.10";
        var monitor = new MonitorDeAutenticacao();
        var chamadasJwt = 0;
        using var raiz = new ApiComLimites(new Dictionary<string, string?>(fixture.Database.Configuration)
        {
            ["AllowedHosts"] = host,
            ["TrustedProxies:Enabled"] = "true",
            ["TrustedProxies:Addresses:0"] = "192.0.2.5",
            ["RateLimiting:LoginPorOrigem"] = "1"
        }, new RelogioDeTeste(), monitor);
        using var api = raiz.WithWebHostBuilder(builder => builder.UseEnvironment(Environments.Production)
            .ConfigureServices(services => services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
                options => options.Events.OnMessageReceived = _ =>
                {
                    Interlocked.Increment(ref chamadasJwt);
                    return Task.CompletedTask;
                })));
        using var client = api.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        await VerificarAsync("http", Origem, OutraOrigem, "https", HttpStatusCode.BadRequest, hsts: false);
        await VerificarAsync("http", proxy, null, "https", HttpStatusCode.BadRequest, hsts: false);
        await VerificarAsync("http", proxy, clienteEncaminhado, "http", HttpStatusCode.BadRequest, hsts: false);
        await VerificarAsync("http", proxy, $"203.0.113.20, {clienteEncaminhado}", "http, https",
            HttpStatusCode.BadRequest, hsts: false);
        await VerificarAsync("http", proxy, clienteEncaminhado, "https", HttpStatusCode.BadRequest, hsts: false,
            hostEnviado: "outro.example.test");
        Assert.Equal(0, chamadasJwt);
        Assert.Equal(0, monitor.Logins);

        await VerificarAsync("https", Origem, OutraOrigem, "http", HttpStatusCode.Unauthorized);
        await VerificarAsync("https", Origem, "203.0.113.20", "http", HttpStatusCode.TooManyRequests);
        await VerificarAsync("http", proxy, clienteEncaminhado, "https", HttpStatusCode.Unauthorized);
        await VerificarAsync("http", proxy, $"::ffff:{clienteEncaminhado}", "HTTPS", HttpStatusCode.TooManyRequests);
        await VerificarAsync("http", proxy, "198.51.100.11", "https", HttpStatusCode.Unauthorized);
        Assert.Equal(3, chamadasJwt);
        Assert.Equal(3, monitor.Logins);

        async Task VerificarAsync(string esquema, string direto, string? encaminhado, string proto,
            HttpStatusCode esperado, bool hsts = true, string hostEnviado = host)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{esquema}://{hostEnviado}{Login}")
            {
                Content = JsonContent.Create(NovoLogin())
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "sentinela-de-transporte");
            request.Headers.Add("X-Test-Remote-Ip", direto);
            request.Headers.Add("X-Forwarded-Proto", proto);
            request.Headers.Add("X-Forwarded-Host", host);
            if (encaminhado is not null)
                request.Headers.Add("X-Forwarded-For", encaminhado);

            using var response = await client.SendAsync(request);
            Assert.Equal(esperado, response.StatusCode);
            Assert.Null(response.Headers.Location);
            TransporteHttpTests.VerificarCabecalhos(response, hsts);
            if (hostEnviado == host)
                Assert.True(response.Headers.CacheControl?.NoStore);
            if (esperado == HttpStatusCode.TooManyRequests)
                await VerificarRejeicaoAsync(response, segundos: 60);
        }
    }

    [SqlServerFact]
    public async Task Login_IsolaOrigemIgnoraCabecalhosERecusaAntesDoJson()
    {
        var relogio = new RelogioDeTeste();
        var monitor = new MonitorDeAutenticacao();
        using var api = CriarApi(relogio, monitor, ("LoginPorOrigem", "2"));
        using var client = CriarCliente(api);

        using var primeira = await EnviarLoginAsync(client, NovoLogin(), Origem);
        using var segunda = await EnviarLoginAsync(client, NovoLogin(), "::ffff:192.0.2.10");
        Assert.Equal(HttpStatusCode.Unauthorized, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, segunda.StatusCode);

        using var invalida = new HttpRequestMessage(HttpMethod.Post, Login)
        {
            Content = new StringContent("{".PadRight(16_385), Encoding.UTF8, "application/json")
        };
        invalida.Headers.Add("X-Test-Remote-Ip", Origem);
        invalida.Headers.Add("X-Forwarded-For", OutraOrigem);
        invalida.Headers.Add("Forwarded", $"for={OutraOrigem}");
        invalida.Headers.Add("X-Real-IP", OutraOrigem);
        using var bloqueada = await client.SendAsync(invalida);
        await VerificarRejeicaoAsync(bloqueada, segundos: 60);
        Assert.Equal(2, monitor.Logins);

        using var outra = await EnviarLoginAsync(client, NovoLogin(), OutraOrigem);
        Assert.Equal(HttpStatusCode.Unauthorized, outra.StatusCode);

        relogio.Avancar(TimeSpan.FromSeconds(58.5));
        using var antes = await EnviarLoginAsync(client, NovoLogin(), Origem);
        await VerificarRejeicaoAsync(antes, segundos: 2);
        relogio.Avancar(TimeSpan.FromSeconds(1.5));
        using var recuperada = await EnviarLoginAsync(client, NovoLogin(), Origem);
        Assert.Equal(HttpStatusCode.Unauthorized, recuperada.StatusCode);
        Assert.Equal(4, monitor.Logins);
    }

    [SqlServerFact]
    public async Task Login_CotaNormalizadaIndependeDeOrigemExistenciaDaContaEConsultaAoBanco()
    {
        var conta = AutorizacaoApiTests.NovaConta();
        await using (var db = fixture.Database.CreateContext())
            await fixture.Database.CreateAuthService(db).RegistrarAsync(conta);

        var monitor = new MonitorDeAutenticacao();
        using var logs = new CapturaDeLogs();
        using var raiz = CriarApi(new RelogioDeTeste(), monitor, ("LoginPorIdentificador", "1"));
        using var api = raiz.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging
            .AddProvider(logs)
            .AddFilter<CapturaDeLogs>((_, _) => true)));
        using var client = CriarCliente(api);
        var existente = NovoLogin(conta.Email);
        var inexistente = NovoLogin();

        using var invalida = await EnviarLoginAsync(client, new LoginRequest
        {
            Email = existente.Email,
            Senha = ""
        }, Origem);
        Assert.Equal(HttpStatusCode.BadRequest, invalida.StatusCode);
        Assert.Equal(0, monitor.Logins);

        using var falhaExistente = await EnviarLoginAsync(client, existente, Origem);
        using var falhaInexistente = await EnviarLoginAsync(client, inexistente, Origem);
        Assert.Equal(HttpStatusCode.Unauthorized, falhaExistente.StatusCode);
        Assert.Equal(falhaExistente.StatusCode, falhaInexistente.StatusCode);
        Assert.Equal(await falhaExistente.Content.ReadAsStringAsync(), await falhaInexistente.Content.ReadAsStringAsync());

        // A variante da conta real teria sucesso se a chamada chegasse ao serviço.
        var emailNormalizadoNaCota = JsonSerializer.Serialize($" {existente.Email.ToUpperInvariant()} ");
        var senhaCorreta = JsonSerializer.Serialize(conta.Senha);
        using var tentativaDuplicada = new HttpRequestMessage(HttpMethod.Post, Login)
        {
            Content = new StringContent(
                $$"""{"email":"{{Guid.NewGuid():N}}@example.test","EMAIL":{{emailNormalizadoNaCota}},"senha":{{senhaCorreta}}}""",
                Encoding.UTF8, "application/json")
        };
        tentativaDuplicada.Headers.Add("X-Test-Remote-Ip", OutraOrigem);
        using var bloqueioExistente = await client.SendAsync(tentativaDuplicada);
        using var bloqueioInexistente = await EnviarLoginAsync(client, inexistente, OutraOrigem);
        var corpo = await VerificarRejeicaoAsync(bloqueioExistente, segundos: 60);
        Assert.Equal(corpo, await VerificarRejeicaoAsync(bloqueioInexistente, segundos: 60));
        Assert.Equal(2, monitor.Logins);
        foreach (var dado in new[] { existente.Email, inexistente.Email, existente.Senha, inexistente.Senha, conta.Senha })
        {
            Assert.DoesNotContain(dado, corpo, StringComparison.OrdinalIgnoreCase);
            Assert.All(logs.Mensagens, mensagem => Assert.DoesNotContain(dado, mensagem, StringComparison.OrdinalIgnoreCase));
        }
    }

    [SqlServerFact]
    public async Task Login_OrigemEsgotadaNaoConsomeOrcamentoGlobal()
    {
        var monitor = new MonitorDeAutenticacao();
        using var api = CriarApi(new RelogioDeTeste(), monitor, ("LoginPorOrigem", "1"), ("GlobalPorJanela", "3"));
        using var client = CriarCliente(api);

        using var primeiro = await EnviarLoginAsync(client, NovoLogin(), Origem);
        Assert.Equal(HttpStatusCode.Unauthorized, primeiro.StatusCode);
        for (var tentativa = 0; tentativa < 3; tentativa++)
        {
            using var excesso = await EnviarLoginAsync(client, NovoLogin(), Origem);
            await VerificarRejeicaoAsync(excesso, segundos: 60);
        }

        using var outraOrigem = await EnviarLoginAsync(client, NovoLogin(), OutraOrigem);
        Assert.Equal(HttpStatusCode.Unauthorized, outraOrigem.StatusCode);
        using var operacao = await EnviarAsync(client, HttpMethod.Get, Produtos, fixture.OperatorToken);
        Assert.Equal(HttpStatusCode.OK, operacao.StatusCode);
        using var global = await EnviarLoginAsync(client, NovoLogin(), "2001:db8::10");
        await VerificarRejeicaoAsync(global, segundos: 60);
        Assert.Equal(2, monitor.Logins);
    }

    [SqlServerFact]
    public async Task Login_TabelaDeIdentificadoresPreservaCotasEVoltaAAdmitirNovasChaves()
    {
        var relogio = new RelogioDeTeste();
        var monitor = new MonitorDeAutenticacao();
        using var api = CriarApi(relogio, monitor, ("MaximoChavesPorJanela", "2"), ("LoginPorIdentificador", "2"));
        using var client = CriarCliente(api);
        var primeiro = NovoLogin();
        var segundo = NovoLogin();
        var terceiro = NovoLogin();

        using var admitido = await EnviarLoginAsync(client, primeiro, Origem);
        using var admitidoSegundaChave = await EnviarLoginAsync(client, segundo, Origem);
        Assert.Equal(HttpStatusCode.Unauthorized, admitido.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, admitidoSegundaChave.StatusCode);
        using var saturada = await EnviarLoginAsync(client, terceiro, Origem);
        await VerificarRejeicaoAsync(saturada, segundos: 60);
        using var cotaPreservada = await EnviarLoginAsync(client, primeiro, Origem);
        Assert.Equal(HttpStatusCode.Unauthorized, cotaPreservada.StatusCode);
        using var cotaEsgotada = await EnviarLoginAsync(client, primeiro, Origem);
        await VerificarRejeicaoAsync(cotaEsgotada, segundos: 60);
        Assert.Equal(3, monitor.Logins);

        relogio.Avancar(TimeSpan.FromMinutes(1));
        using var recuperada = await EnviarLoginAsync(client, terceiro, Origem);
        Assert.Equal(HttpStatusCode.Unauthorized, recuperada.StatusCode);
        Assert.Equal(4, monitor.Logins);
    }

    [SqlServerFact]
    public async Task OperacoesECadastro_IsolamSubECotasEPreservamAutorizacao()
    {
        var monitor = new MonitorDeAutenticacao();
        using var api = CriarApi(new RelogioDeTeste(), monitor, ("CadastroPorAdmin", "1"), ("OperacoesPorUsuario", "1"));
        using var client = CriarCliente(api);
        using var produtos = await EnviarAsync(client, HttpMethod.Get, Produtos, fixture.AdminToken);
        Assert.Equal(HttpStatusCode.OK, produtos.StatusCode);
        using var compradores = await EnviarAsync(client, HttpMethod.Get, Compradores, fixture.AdminToken);
        await VerificarRejeicaoAsync(compradores, segundos: 60);

        var leitor = new JwtSecurityTokenHandler();
        var jwt = fixture.Database.CreateJwtConfiguration();
        var sub = leitor.ReadJwtToken(fixture.AdminToken).Subject;
        var renovado = leitor.WriteToken(new JwtSecurityToken(jwt.Issuer, jwt.Audience,
            [new Claim("sub", sub.ToUpperInvariant()), new Claim("role", "Admin")],
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: jwt.SigningCredentials));
        using var mesmoUsuario = await EnviarAsync(client, HttpMethod.Get, Produtos, renovado);
        await VerificarRejeicaoAsync(mesmoUsuario, segundos: 60);

        using var operador = await EnviarAsync(client, HttpMethod.Get, Compradores, fixture.OperatorToken);
        Assert.Equal(HttpStatusCode.OK, operador.StatusCode);
        using var invalido = await EnviarAsync(client, HttpMethod.Get, Produtos, "token-invalido");
        Assert.Equal(HttpStatusCode.Unauthorized, invalido.StatusCode);

        var conta = AutorizacaoApiTests.NovaConta();
        using var proibido = await EnviarAsync(client, HttpMethod.Post, Cadastro, fixture.OperatorToken, conta);
        Assert.Equal(HttpStatusCode.Forbidden, proibido.StatusCode);
        using var cadastro = await EnviarAsync(client, HttpMethod.Post, Cadastro, fixture.AdminToken, conta);
        Assert.Equal(HttpStatusCode.NoContent, cadastro.StatusCode);
        using var bloqueado = await EnviarAsync(client, HttpMethod.Post, Cadastro, fixture.AdminToken, AutorizacaoApiTests.NovaConta());
        await VerificarRejeicaoAsync(bloqueado, segundos: 60);
        Assert.Equal(1, monitor.Cadastros);

        var outroAdmin = fixture.CriarTokenDeTeste(["Admin"]);
        using var conflito = await EnviarAsync(client, HttpMethod.Post, Cadastro, outroAdmin, conta);
        Assert.Equal(HttpStatusCode.Conflict, conflito.StatusCode);
        Assert.Equal(2, monitor.Cadastros);
    }

    [SqlServerTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concorrencia_RejeitaSemFilaNemRetryAfterELiberaAoEncerrar(bool global)
    {
        var iniciou = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var liberar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var monitor = new MonitorDeAutenticacao(async () =>
        {
            iniciou.TrySetResult();
            await liberar.Task.WaitAsync(TimeSpan.FromSeconds(10));
        });
        using var api = CriarApi(new RelogioDeTeste(), monitor, (global ? "GlobalConcorrente" : "LoginConcorrente", "1"));
        using var client = CriarCliente(api);
        var emCurso = EnviarLoginAsync(client, NovoLogin(), Origem);
        try
        {
            await iniciou.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var acimaDoLimite = CriarLoginAcimaDoLimite();
            using var rejeitada = global
                ? await EnviarAsync(client, HttpMethod.Get, Produtos, fixture.OperatorToken)
                : await client.SendAsync(acimaDoLimite);
            await VerificarRejeicaoAsync(rejeitada, segundos: null);
            Assert.Equal(1, monitor.Logins);

            using var saude = await client.GetAsync(Saude);
            Assert.Equal(HttpStatusCode.OK, saude.StatusCode);
        }
        finally
        {
            liberar.TrySetResult();
            using var concluida = await emCurso;
            Assert.Equal(HttpStatusCode.Unauthorized, concluida.StatusCode);
        }

        using var aposLiberar = CriarLoginAcimaDoLimite();
        using var acimaDoLimiteSemConcorrencia = await client.SendAsync(aposLiberar);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, acimaDoLimiteSemConcorrencia.StatusCode);
        using var recuperada = await EnviarLoginAsync(client, NovoLogin(), OutraOrigem);
        Assert.Equal(HttpStatusCode.Unauthorized, recuperada.StatusCode);
        Assert.Equal(2, monitor.Logins);

        static HttpRequestMessage CriarLoginAcimaDoLimite()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, Login)
            {
                Content = new StringContent("{}".PadRight(16_385), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-Test-Remote-Ip", OutraOrigem);
            return request;
        }
    }

    [SqlServerFact]
    public async Task Saude_TemFrequenciaEConcorrenciaSeparadasDaApi()
    {
        var relogio = new RelogioDeTeste();
        var sonda = new SondaObservavel();
        using var raiz = CriarApi(relogio, new MonitorDeAutenticacao(),
            ("GlobalPorJanela", "1"), ("SaudePorJanela", "1"), ("LivenessPorJanela", "2"));
        using var api = raiz.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddHealthChecks().AddCheck("sonda-teste", sonda, tags: [SondasDeSaude.Readiness])));
        using var client = CriarCliente(api);
        var emCurso = client.GetAsync(Saude);
        try
        {
            await sonda.Iniciou.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var concorrente = await client.GetAsync("/health/ready");
            await VerificarRejeicaoAsync(concorrente, segundos: null);
            using var operacao = await EnviarAsync(client, HttpMethod.Get, Produtos, fixture.OperatorToken);
            Assert.Equal(HttpStatusCode.OK, operacao.StatusCode);
            using var liveness = await client.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, liveness.StatusCode);
        }
        finally
        {
            sonda.Liberar.TrySetResult();
            using var concluida = await emCurso;
            Assert.Equal(HttpStatusCode.OK, concluida.StatusCode);
        }

        using var foraDaCota = await client.GetAsync("/health/ready");
        await VerificarRejeicaoAsync(foraDaCota, segundos: 60);
        using var livenessDisponivel = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, livenessDisponivel.StatusCode);
        using var head = new HttpRequestMessage(HttpMethod.Head, "/health/live");
        using var livenessForaDaCota = await client.SendAsync(head);
        Assert.Equal(HttpStatusCode.TooManyRequests, livenessForaDaCota.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(60), livenessForaDaCota.Headers.RetryAfter?.Delta);
        Assert.True(livenessForaDaCota.Headers.CacheControl?.NoStore);
        Assert.Empty(await livenessForaDaCota.Content.ReadAsStringAsync());
        Assert.Equal(1, sonda.Execucoes);
        using var foraDaCotaGlobal = await EnviarAsync(client, HttpMethod.Get, Produtos, fixture.AdminToken);
        await VerificarRejeicaoAsync(foraDaCotaGlobal, segundos: 60);

        relogio.Avancar(TimeSpan.FromMinutes(1));
        using var recuperada = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, recuperada.StatusCode);
        Assert.Equal(2, sonda.Execucoes);
    }

    private ApiComLimites CriarApi(TimeProvider relogio, MonitorDeAutenticacao monitor, params (string Chave, string Valor)[] limites)
    {
        var config = new Dictionary<string, string?>(fixture.Database.Configuration);
        foreach (var (chave, valor) in limites)
            config[$"RateLimiting:{chave}"] = valor;
        return new ApiComLimites(config, relogio, monitor);
    }

    private static HttpClient CriarCliente(WebApplicationFactory<Program> api) => api.CreateClient(
        new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    private static LoginRequest NovoLogin(string? email = null) => new()
    {
        Email = email ?? $"{Guid.NewGuid():N}@example.test",
        Senha = $"Invalida para teste {Guid.NewGuid():N}"
    };

    private static Task<HttpResponseMessage> EnviarLoginAsync(HttpClient client, LoginRequest request, string origem) =>
        EnviarAsync(client, HttpMethod.Post, Login, token: null, request, origem);

    private static async Task<HttpResponseMessage> EnviarAsync(
        HttpClient client, HttpMethod method, string rota, string? token, object? corpo = null, string? origem = null)
    {
        using var request = new HttpRequestMessage(method, rota);
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (corpo is not null)
            request.Content = JsonContent.Create(corpo);
        if (origem is not null)
            request.Headers.Add("X-Test-Remote-Ip", origem);
        return await client.SendAsync(request);
    }

    private static async Task<string> VerificarRejeicaoAsync(HttpResponseMessage response, int? segundos)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(segundos, response.Headers.RetryAfter?.Delta?.TotalSeconds);
        Assert.Empty(response.Headers.WwwAuthenticate);
        var corpo = await response.Content.ReadAsStringAsync();
        using var json = System.Text.Json.JsonDocument.Parse(corpo);
        var mensagem = Assert.Single(json.RootElement.EnumerateObject());
        Assert.Equal("mensagem", mensagem.Name);
        Assert.Equal("Muitas requisições. Tente novamente mais tarde.", mensagem.Value.GetString());
        return corpo;
    }

    private sealed class ApiComLimites(
        Dictionary<string, string?> config, TimeProvider relogio, MonitorDeAutenticacao monitor) : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(config));
            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.ConfigureServices(services =>
        {
            services.AddSingleton(relogio);
            services.AddSingleton<IStartupFilter, OrigemDeTeste>();
            services.AddScoped<IAuthService>(provider => new AutenticacaoObservavel(provider.GetRequiredService<AuthService>(), monitor));
        });
    }

    private sealed class OrigemDeTeste : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, continuar) =>
            {
                if (IPAddress.TryParse(context.Request.Headers["X-Test-Remote-Ip"], out var ip))
                    context.Connection.RemoteIpAddress = ip;
                return continuar(context);
            });
            next(app);
        };
    }

    private sealed class MonitorDeAutenticacao(Func<Task>? antesDoLogin = null)
    {
        public int Logins;
        public int Cadastros;
        public Task AntesDoLoginAsync() => antesDoLogin?.Invoke() ?? Task.CompletedTask;
    }

    private sealed class AutenticacaoObservavel(IAuthService auth, MonitorDeAutenticacao monitor) : IAuthService
    {
        public Task RegistrarAsync(RegistrarUsuarioRequest request)
        {
            Interlocked.Increment(ref monitor.Cadastros);
            return auth.RegistrarAsync(request);
        }

        public async Task<LoginResponse> LoginAsync(LoginRequest request)
        {
            Interlocked.Increment(ref monitor.Logins);
            await monitor.AntesDoLoginAsync();
            return await auth.LoginAsync(request);
        }
    }

    private sealed class CapturaDeLogs : ILoggerProvider, ILogger
    {
        public ConcurrentQueue<string> Mensagens { get; } = new();
        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Mensagens.Enqueue(formatter(state, exception));
        public void Dispose() { }
    }

    private sealed class SondaObservavel : IHealthCheck
    {
        public int Execucoes;
        public TaskCompletionSource Iniciou { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Liberar { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Execucoes);
            Iniciou.TrySetResult();
            await Liberar.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            return HealthCheckResult.Healthy();
        }
    }
}

public class ConfiguracaoDeLimitesTests
{
    [Theory]
    [InlineData("JanelaSegundos", "0")]
    [InlineData("JanelaSegundos", "3601")]
    [InlineData("GlobalPorJanela", "-1")]
    [InlineData("GlobalPorJanela", "100001")]
    [InlineData("LoginConcorrente", "0")]
    [InlineData("GlobalConcorrente", "2147483647")]
    [InlineData("SaudeConcorrente", "1025")]
    [InlineData("MaximoChavesPorJanela", "100001")]
    [InlineData("SaudePorJanela", "0")]
    public void Startup_RejeitaConfiguracaoInvalidaAntesDeAcessarBanco(string chave, string valor)
    {
        using var factory = new AutorizacaoApiFixture.ApiFactory(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=127.0.0.1,1;Connect Timeout=1",
            ["Jwt:Chave"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            [$"RateLimiting:{chave}"] = valor
        });

        var exception = Assert.Throws<ValidationException>(() => factory.CreateClient());
        Assert.Contains(chave, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("quatro")]
    [InlineData("")]
    [InlineData("2147483648")]
    public void Configuracao_RejeitaLimiteQueNaoSejaInteiroRepresentavel(string valor)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:LoginConcorrente"] = valor
        }).Build();

        var exception = Assert.Throws<InvalidOperationException>(() => OpcoesDeLimites.Carregar(config));
        Assert.Contains("RateLimiting:LoginConcorrente", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Configuracao_SecaoAusenteUsaPadroes()
    {
        var opcoes = OpcoesDeLimites.Carregar(new ConfigurationBuilder().Build());

        Assert.Equal(TimeSpan.FromSeconds(60), opcoes.Janela);
        Assert.Equal(1200, opcoes.GlobalPorJanela);
        Assert.Equal(32, opcoes.GlobalConcorrente);
        Assert.Equal(20, opcoes.LoginPorOrigem);
        Assert.Equal(5, opcoes.LoginPorIdentificador);
        Assert.Equal(4, opcoes.LoginConcorrente);
        Assert.Equal(10, opcoes.CadastroPorAdmin);
        Assert.Equal(120, opcoes.OperacoesPorUsuario);
        Assert.Equal(30, opcoes.SaudePorJanela);
        Assert.Equal(1, opcoes.SaudeConcorrente);
        Assert.Equal(30, opcoes.LivenessPorJanela);
        Assert.Equal(1, opcoes.LivenessConcorrente);
        Assert.Equal(1024, opcoes.MaximoChavesPorJanela);
    }

    [Fact]
    public void Configuracao_RejeitaSecaoEscalarEOpcaoDesconhecida()
    {
        var escalar = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting"] = "desabilitado"
        }).Build();
        var desconhecida = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:LoginPorOrgem"] = "10"
        }).Build();

        Assert.Throws<ValidationException>(() => OpcoesDeLimites.Carregar(escalar));
        Assert.Throws<InvalidOperationException>(() => OpcoesDeLimites.Carregar(desconhecida));
    }

    [Fact]
    public void Origem_NormalizaIpMapeadoEIpv6EIsolaConexoesSemEndereco()
    {
        var cotas = new CotasDeRequisicao(new OpcoesDeLimites { LoginPorOrigem = 1 }, new RelogioDeTeste());
        var espera = TimeSpan.FromMinutes(1);

        Assert.Null(cotas.ConsumirOrigem(IPAddress.Parse("192.0.2.10")));
        Assert.Equal(espera, cotas.ConsumirOrigem(IPAddress.Parse("::ffff:192.0.2.10")));
        Assert.Null(cotas.ConsumirOrigem(IPAddress.Parse("2001:db8::1")));
        Assert.Equal(espera, cotas.ConsumirOrigem(IPAddress.Parse("2001:0db8:0:0:0:0:0:1")));
        Assert.Null(cotas.ConsumirOrigem(IPAddress.Parse("2001:db8::2")));
        Assert.Null(cotas.ConsumirOrigem(null));
        Assert.Equal(espera, cotas.ConsumirOrigem(null));
        Assert.Null(cotas.ConsumirOrigem(IPAddress.None));
    }

    [Fact]
    public void Janela_ImpoeCardinalidadeSemRemoverContadoresERecuperaNoLimiteExato()
    {
        var relogio = new RelogioDeTeste();
        var janela = TimeSpan.FromMinutes(1);
        var cotas = new JanelaPorChave<int>(relogio, janela, limite: 2, maximoChaves: 2);
        Assert.Null(cotas.TentarConsumir(1));
        Assert.Null(cotas.TentarConsumir(2));
        for (var chave = 3; chave < 100; chave++)
            Assert.Equal(janela, cotas.TentarConsumir(chave));
        Assert.Null(cotas.TentarConsumir(1));
        Assert.Equal(janela, cotas.TentarConsumir(1));

        relogio.Avancar(janela - TimeSpan.FromTicks(1));
        Assert.Equal(TimeSpan.FromTicks(1), cotas.TentarConsumir(3));
        relogio.Avancar(TimeSpan.FromTicks(1));
        Assert.Null(cotas.TentarConsumir(3));
        Assert.Null(cotas.TentarConsumir(1));
    }

    [Fact]
    public async Task Janela_RenovacaoConcorrenteMantemACotaNoLimiteExato()
    {
        const int limite = 7;
        const int tentativas = 64;
        var relogio = new RelogioDeTeste();
        var janela = TimeSpan.FromSeconds(1);
        var cotas = new JanelaPorChave<int>(relogio, janela, limite, maximoChaves: 1);

        for (var i = 0; i < limite; i++)
            Assert.Null(cotas.TentarConsumir(1));
        Assert.Equal(janela, cotas.TentarConsumir(1));

        relogio.Avancar(janela);
        var inicio = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var concorrentes = Enumerable.Range(0, tentativas).Select(async _ =>
        {
            await inicio.Task;
            return cotas.TentarConsumir(2);
        }).ToArray();
        inicio.SetResult();

        var resultados = await Task.WhenAll(concorrentes);
        Assert.Equal(limite, resultados.Count(espera => espera is null));
        Assert.Equal(tentativas - limite, resultados.Count(espera => espera == janela));
        Assert.Equal(janela, cotas.TentarConsumir(1));
    }
}

public class PermissoesDeConcorrenciaTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concorrencia_LiberaPermissoesGlobalEDeLoginAposFalhaOuCancelamento(bool cancelar)
    {
        var iniciou = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saiu = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = new HostBuilder().ConfigureWebHost(builder => builder
            .UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AdicionarLimitesHttp(new OpcoesDeLimites { GlobalConcorrente = 1, LoginConcorrente = 1 });
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.Use(async (context, next) =>
                {
                    if (!context.Request.Query.ContainsKey("falhar"))
                    {
                        await next(context);
                        return;
                    }

                    try
                    {
                        await next(context);
                    }
                    finally
                    {
                        saiu.TrySetResult();
                    }
                });
                app.UseRateLimiter();
                app.UseEndpoints(endpoints => endpoints.MapGet("/login", async context =>
                {
                    if (!context.Request.Query.ContainsKey("falhar"))
                        return;

                    iniciou.TrySetResult();
                    if (cancelar)
                        await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
                    else
                        throw new InvalidOperationException("Falha de teste.");
                }).RequireRateLimiting(LimitesHttp.Login));
            }))
            .Build();
        await host.StartAsync();
        using var client = host.GetTestClient();
        using var cancelamento = new CancellationTokenSource();

        var falha = client.GetAsync("/login?falhar", cancelamento.Token);
        await iniciou.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (cancelar)
        {
            cancelamento.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => falha);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => falha);
        }

        await saiu.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var recuperada = await client.GetAsync("/login");
        Assert.Equal(HttpStatusCode.OK, recuperada.StatusCode);
    }
}

internal sealed class RelogioDeTeste : TimeProvider
{
    private long _timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => Interlocked.Read(ref _timestamp);
    public void Avancar(TimeSpan intervalo) => Interlocked.Add(ref _timestamp, intervalo.Ticks);
}
