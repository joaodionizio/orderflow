using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using OrderFlow.Api.Health;
using OrderFlow.Api.Middlewares;
using OrderFlow.Infrastructure.Data;
using HealthChecks.SqlServer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Swagger;

namespace OrderFlow.Tests;

public sealed class ExposicaoApiTests(AutorizacaoApiFixture fixture) : IClassFixture<AutorizacaoApiFixture>
{
    private const string HostPermitido = "orderflow.example.test";
    private static readonly string[] Sondas = ["/health/live", "/health/ready", "/health"];

    [SqlServerTheory]
    [InlineData(nameof(Environments.Production))]
    [InlineData(nameof(Environments.Staging))]
    public async Task Swagger_ForaDeDevelopmentNaoPublicaJsonInterfaceNemAssets(string ambiente)
    {
        using var raiz = CriarFabrica();
        using var api = raiz.WithWebHostBuilder(builder => builder.UseEnvironment(ambiente));
        using var client = CriarCliente(api);
        Assert.Null(api.Services.GetService<ISwaggerProvider>());

        foreach (var rota in new[] { "/swagger", "/swagger/index.html", "/swagger/v1/swagger.json", "/swagger/swagger-ui-bundle.js" })
        {
            using var anonimo = await client.GetAsync(rota);
            Assert.Equal(HttpStatusCode.Unauthorized, anonimo.StatusCode);
            Assert.Empty(await anonimo.Content.ReadAsStringAsync());
            TransporteHttpTests.VerificarCabecalhos(anonimo, hsts: true);

            using var request = new HttpRequestMessage(HttpMethod.Get, rota);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.AdminToken);
            using var admin = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, admin.StatusCode);
            Assert.Empty(await admin.Content.ReadAsStringAsync());
            TransporteHttpTests.VerificarCabecalhos(admin, hsts: true);
        }
    }

    [SqlServerTheory]
    [InlineData(nameof(Environments.Production))]
    [InlineData(nameof(Environments.Staging))]
    public async Task Saude_EAnonimaComTlsEHostPermitido(string ambiente)
    {
        using var raiz = CriarFabrica();
        using var api = raiz.WithWebHostBuilder(builder => builder.UseEnvironment(ambiente));
        using var client = CriarCliente(api);

        foreach (var rota in Sondas)
        {
            using var saudavel = await client.GetAsync(rota);
            await VerificarSondaAsync(saudavel, HttpStatusCode.OK, "Healthy");
            TransporteHttpTests.VerificarCabecalhos(saudavel, hsts: true);

            using var head = new HttpRequestMessage(HttpMethod.Head, rota);
            using var respostaHead = await client.SendAsync(head);
            Assert.Equal(HttpStatusCode.OK, respostaHead.StatusCode);
            Assert.Empty(await respostaHead.Content.ReadAsStringAsync());
            Assert.True(respostaHead.Headers.CacheControl?.NoStore);
            Assert.Equal(saudavel.Content.Headers.ContentType, respostaHead.Content.Headers.ContentType);

            using var semTls = new HttpRequestMessage(HttpMethod.Get, $"http://{HostPermitido}{rota}");
            semTls.Headers.Add("X-Forwarded-For", "192.0.2.50");
            semTls.Headers.Add("X-Forwarded-Proto", "https");
            using var tlsFalsificado = await client.SendAsync(semTls);
            Assert.Equal(HttpStatusCode.BadRequest, tlsFalsificado.StatusCode);

            using var hostErrado = await client.GetAsync($"https://outro.example.test{rota}");
            Assert.Equal(HttpStatusCode.BadRequest, hostErrado.StatusCode);
        }
    }

    [SqlServerFact]
    public async Task Saude_SqlServerInacessivelAfetaApenasReadinessSemExporDetalhes()
    {
        using var portaSemSql = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        portaSemSql.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var porta = Assert.IsType<IPEndPoint>(portaSemSql.LocalEndPoint).Port;
        var conexao = new SqlConnectionStringBuilder(fixture.Database.Configuration["ConnectionStrings:DefaultConnection"])
        {
            DataSource = $"127.0.0.1,{porta}",
            ConnectTimeout = 1,
            ConnectRetryCount = 0
        };
        using var logs = new LogsObservaveis();
        using var raiz = CriarFabrica(conexao.ConnectionString);
        using var api = raiz.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Production);
            // O startup continua executando migrations no banco real isolado.
            builder.ConfigureServices(services => services.AddScoped<ApplicationDbContext>(_ => fixture.Database.CreateContext()));
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
        });
        using var client = CriarCliente(api);
        logs.Registros.Clear();

        using var live = await client.GetAsync("/health/live");
        await VerificarSondaAsync(live, HttpStatusCode.OK, "Healthy");
        Assert.DoesNotContain(logs.Registros, registro => registro.Categoria.Contains("SondaSqlServer"));

        foreach (var rota in new[] { "/health/ready", "/health" })
        {
            using var indisponivel = await client.GetAsync(rota);
            await VerificarSondaAsync(indisponivel, HttpStatusCode.ServiceUnavailable, "Unhealthy");
        }

        var falhasSql = logs.Registros.Where(registro => registro.Categoria.Contains("SondaSqlServer")).ToArray();
        Assert.Equal(2, falhasSql.Length);
        Assert.All(falhasSql, registro => Assert.Contains("SqlException", registro.Mensagem));
        Assert.All(logs.Registros, registro =>
        {
            Assert.Null(registro.Excecao);
            Assert.False(registro.Mensagem.Contains(conexao.Password), "A senha apareceu nos logs.");
            Assert.False(registro.Mensagem.Contains(conexao.DataSource), "O endereço SQL apareceu nos logs.");
            Assert.False(registro.Mensagem.Contains(conexao.InitialCatalog), "O catálogo SQL apareceu nos logs.");
        });
    }

    [SqlServerFact]
    public async Task Saude_DegradedRetorna503ERedigeDetalhes()
    {
        using var logs = new LogsObservaveis();
        using var api = fixture.Api.WithWebHostBuilder(builder => builder
            .ConfigureLogging(logging => logging.AddProvider(logs))
            .ConfigureServices(services => services.AddHealthChecks()
                .AddCheck("dependencia-interna", () => HealthCheckResult.Degraded("detalhe-interno",
                    new InvalidOperationException("detalhe-interno")), tags: [SondasDeSaude.Readiness])));
        using var client = api.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var resultado = await client.GetAsync("/health/ready");
        await VerificarSondaAsync(resultado, HttpStatusCode.ServiceUnavailable, "Degraded");
        using var alias = await client.GetAsync("/health");
        await VerificarSondaAsync(alias, HttpStatusCode.ServiceUnavailable, "Degraded");
        using var head = new HttpRequestMessage(HttpMethod.Head, "/health/ready");
        using var respostaHead = await client.SendAsync(head);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, respostaHead.StatusCode);
        Assert.True(respostaHead.Headers.CacheControl?.NoStore);
        Assert.Empty(await respostaHead.Content.ReadAsStringAsync());
        using var live = await client.GetAsync("/health/live");
        await VerificarSondaAsync(live, HttpStatusCode.OK, "Healthy");
        Assert.DoesNotContain(logs.Registros, log => log.Mensagem.Contains("detalhe-interno") || log.Excecao is not null);
    }

    [SqlServerTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Readiness_InterrompeConsultaSqlELiberaConexao(bool clienteCancela)
    {
        var conexao = new SqlConnectionStringBuilder(fixture.Database.Configuration["ConnectionStrings:DefaultConnection"])
        {
            ApplicationName = $"OrderFlowReadiness-{Guid.NewGuid():N}",
            ConnectTimeout = 15,
            MaxPoolSize = 1
        };
        await using var monitor = new SqlConnection(fixture.Database.Configuration["ConnectionStrings:DefaultConnection"]);
        await using var comando = monitor.CreateCommand();
        comando.CommandText = """
            SELECT COUNT(*) FROM sys.dm_exec_requests r
            JOIN sys.dm_exec_sessions s ON r.session_id = s.session_id
            WHERE s.program_name = @aplicacao AND r.command = 'WAITFOR'
            """;
        comando.Parameters.AddWithValue("@aplicacao", conexao.ApplicationName);
        using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await monitor.OpenAsync(limite.Token);
        await using var aquecimento = new SqlConnection(conexao.ConnectionString);
        await aquecimento.OpenAsync(limite.Token);
        await aquecimento.CloseAsync();

        var sql = new SqlServerHealthCheck(new SqlServerHealthCheckOptions
        {
            ConnectionString = conexao.ConnectionString,
            CommandText = "WAITFOR DELAY '00:00:20'; SELECT 1;"
        });
        using var logs = new LogsObservaveis();
        using var logger = LoggerFactory.Create(logging => logging.AddProvider(logs));
        var sonda = new SondasDeSaude.SondaSqlServer(sql, logger.CreateLogger<SondasDeSaude.SondaSqlServer>());
        var contexto = new HealthCheckContext { Registration = new HealthCheckRegistration("sqlserver", sonda, HealthStatus.Unhealthy, null) };
        using var solicitacao = CancellationTokenSource.CreateLinkedTokenSource(limite.Token);
        var cronometro = Stopwatch.StartNew();
        var execucao = sonda.CheckHealthAsync(contexto, solicitacao.Token);
        try
        {
            while (Convert.ToInt32(await comando.ExecuteScalarAsync(limite.Token)) == 0)
            {
                Assert.False(execucao.IsCompleted, "A sonda terminou antes de executar a consulta SQL.");
                await Task.Delay(20, limite.Token);
            }

            if (clienteCancela)
            {
                solicitacao.Cancel();
                var falha = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => execucao.WaitAsync(TimeSpan.FromSeconds(2)));
                Assert.Equal(solicitacao.Token, falha.CancellationToken);
                Assert.Empty(logs.Registros);
            }
            else
            {
                var resultado = await execucao.WaitAsync(TimeSpan.FromSeconds(6));
                Assert.InRange(cronometro.Elapsed.TotalSeconds, 2.8, 7);
                Assert.Equal(HealthStatus.Unhealthy, resultado.Status);
                Assert.Null(resultado.Exception);
                Assert.Null(Assert.Single(logs.Registros).Excecao);
            }

            Assert.Equal(0, Convert.ToInt32(await comando.ExecuteScalarAsync(limite.Token)));
            using var prazoPool = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await aquecimento.OpenAsync(prazoPool.Token);
        }
        finally
        {
            solicitacao.Cancel();
        }
    }

    [SqlServerFact]
    public async Task Readiness_AplicaPrazoDeTresSegundosAoAbrirConexaoSql()
    {
        using var servidorSemResposta = new TcpListener(IPAddress.Loopback, 0);
        servidorSemResposta.Start();
        var porta = ((IPEndPoint)servidorSemResposta.LocalEndpoint).Port;
        var conexao = new SqlConnectionStringBuilder(fixture.Database.Configuration["ConnectionStrings:DefaultConnection"])
        {
            DataSource = $"tcp:127.0.0.1,{porta}",
            ConnectTimeout = 12,
            ConnectRetryCount = 0
        };
        using var raiz = CriarFabrica(conexao.ConnectionString);
        using var api = raiz.WithWebHostBuilder(builder => builder.UseEnvironment(Environments.Production)
            .ConfigureServices(services => services.AddScoped<ApplicationDbContext>(_ => fixture.Database.CreateContext())));
        using var client = CriarCliente(api);
        using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var cronometro = Stopwatch.StartNew();
        var prontidao = client.GetAsync("/health/ready", limite.Token);
        using var socket = await servidorSemResposta.AcceptTcpClientAsync(limite.Token);
        var encerramento = socket.GetStream().CopyToAsync(Stream.Null, limite.Token);
        using var resposta = await prontidao;
        await VerificarSondaAsync(resposta, HttpStatusCode.ServiceUnavailable, "Unhealthy");
        await encerramento;
        Assert.InRange(cronometro.Elapsed.TotalSeconds, 2.8, 8);
    }

    [SqlServerFact]
    public async Task FalhaInesperada_EmRotaOperacionalRetorna500ERegistraSomenteMetadados()
    {
        using var logs = new LogsObservaveis();
        using var raiz = CriarFabrica();
        using var api = raiz.WithWebHostBuilder(builder => builder.UseEnvironment(Environments.Production)
            .ConfigureLogging(logging => logging.AddFilter("Microsoft.AspNetCore", LogLevel.Trace).AddProvider(logs)));
        using var client = CriarCliente(api);
        const string sentinela = "dados-internos-sensiveis";
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/_test/fallback/falha/{sentinela}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.OperatorToken);

        using var resposta = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.InternalServerError, resposta.StatusCode);
        Assert.True(resposta.Headers.CacheControl?.NoStore);
        Assert.False(resposta.Headers.Contains("X-Dados-Internos"));
        TransporteHttpTests.VerificarCabecalhos(resposta, hsts: true);
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        var mensagem = Assert.Single(json.RootElement.EnumerateObject());
        Assert.Equal("mensagem", mensagem.Name);
        Assert.Equal("Ocorreu um erro interno no servidor.", mensagem.Value.GetString());

        var registro = Assert.Single(logs.Registros, registro => registro.Categoria == typeof(ExceptionMiddleware).FullName);
        Assert.Equal(LogLevel.Error, registro.Nivel);
        Assert.Null(registro.Excecao);
        Assert.Contains("FallbackDeTeste.Falhar", registro.Mensagem);
        Assert.Contains(nameof(InvalidOperationException), registro.Mensagem);
        Assert.Contains(nameof(FormatException), registro.Mensagem);
        Assert.Contains("TraceId: rastreio-de-teste", registro.Mensagem);
        Assert.DoesNotContain(logs.Registros, registro => registro.Mensagem.Contains(sentinela) ||
            registro.Excecao?.ToString().Contains(sentinela) == true);

        using var consulta = new HttpRequestMessage(HttpMethod.Get, "/api/v1/produtos");
        consulta.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.OperatorToken);
        using var consultaValida = await client.SendAsync(consulta);
        Assert.Equal(HttpStatusCode.OK, consultaValida.StatusCode);
        Assert.DoesNotContain(logs.Registros, registro => registro.Categoria == "Microsoft.EntityFrameworkCore.Database.Command");
    }

    private AutorizacaoApiFixture.ApiFactory CriarFabrica(string? conexaoDaSonda = null)
    {
        var configuracao = new Dictionary<string, string?>(fixture.Database.Configuration)
        {
            ["AllowedHosts"] = HostPermitido,
            ["Swagger:Enabled"] = "true"
        };
        if (conexaoDaSonda is not null)
            configuracao["ConnectionStrings:DefaultConnection"] = conexaoDaSonda;
        return new AutorizacaoApiFixture.ApiFactory(configuracao);
    }

    private static HttpClient CriarCliente(WebApplicationFactory<Program> api) => api.CreateClient(
        new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"https://{HostPermitido}"), AllowAutoRedirect = false });

    private static async Task VerificarSondaAsync(HttpResponseMessage resposta, HttpStatusCode codigo, string estado)
    {
        Assert.Equal(codigo, resposta.StatusCode);
        Assert.Equal("application/json", resposta.Content.Headers.ContentType?.MediaType);
        Assert.True(resposta.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        var status = Assert.Single(json.RootElement.EnumerateObject());
        Assert.Equal("status", status.Name);
        Assert.Equal(estado, status.Value.GetString());
    }
}
