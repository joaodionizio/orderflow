using System.Net;
using System.Text.Json;
using OrderFlow.Api.Health;
using OrderFlow.Api.Middlewares;
using OrderFlow.Api.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace OrderFlow.Tests;

public sealed class SondasDeSaudeKestrelTests(AutorizacaoApiFixture fixture) : IClassFixture<AutorizacaoApiFixture>
{
    private const string Liveness = "/health/live";
    private const string Readiness = "/health/ready";
    private const string Alias = "/health";
    private const string CabecalhoDeConexao = "X-Test-Connection";

    [SqlServerTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Saude_RejeitaCorpoAntesDoEnvioELiberaAVaga(bool http2)
    {
        var consultas = 0;
        await using var app = CriarAplicacao(http2, new OpcoesDeLimites(), () => Interlocked.Increment(ref consultas));
        using var prazo = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await app.StartAsync(prazo.Token);
        using var cliente = CriarCliente(app, http2);

        foreach (var rota in new[] { Liveness, Readiness, Alias })
        {
            foreach (var metodo in new[] { HttpMethod.Get, HttpMethod.Head })
            {
                foreach (var tamanho in new int?[] { 2_048, null })
                {
                    var antes = Volatile.Read(ref consultas);
                    using var rejeitada = await EnviarCorpoAsync(cliente, rota, metodo, tamanho, http2, prazo.Token);
                    Assert.Equal(HttpStatusCode.BadRequest, rejeitada.StatusCode);
                    Assert.True(rejeitada.Headers.CacheControl?.NoStore);
                    Assert.Empty(await rejeitada.Content.ReadAsByteArrayAsync(prazo.Token));
                    TransporteHttpTests.VerificarCabecalhos(rejeitada);
                    Assert.Equal(antes, Volatile.Read(ref consultas));

                    var proximaRota = rota switch { Readiness => Alias, Alias => Readiness, _ => Liveness };
                    using var proxima = await SondarAsync(cliente, proximaRota, metodo, tamanho == 2_048, prazo.Token);
                    Assert.Equal(antes + (rota == Liveness ? 0 : 1), Volatile.Read(ref consultas));
                    VerificarConexoes(rejeitada, proxima, http2);
                }

                var antesDoExcesso = Volatile.Read(ref consultas);
                using var excesso = await EnviarCorpoAsync(cliente, rota, metodo, LimitesDeCorpo.Padrao + 1, http2, prazo.Token);
                Assert.Equal(HttpStatusCode.RequestEntityTooLarge, excesso.StatusCode);
                var corpo = await excesso.Content.ReadAsStringAsync(prazo.Token);
                if (metodo == HttpMethod.Head)
                {
                    Assert.Empty(corpo);
                }
                else
                {
                    using var json = JsonDocument.Parse(corpo);
                    var mensagem = Assert.Single(json.RootElement.EnumerateObject());
                    Assert.Equal("mensagem", mensagem.Name);
                    Assert.Equal("O corpo da requisição excede o tamanho permitido.", mensagem.Value.GetString());
                }
                Assert.Equal(antesDoExcesso, Volatile.Read(ref consultas));
            }
        }
    }

    [SqlServerFact]
    public async Task Saude_CorpoRejeitadoConsomeCotaCompartilhadaDeReadiness()
    {
        var consultas = 0;
        var limites = new OpcoesDeLimites { SaudePorJanela = 1, LivenessPorJanela = 1 };
        await using var app = CriarAplicacao(http2: false, limites, () => Interlocked.Increment(ref consultas));
        using var prazo = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await app.StartAsync(prazo.Token);
        using var cliente = CriarCliente(app, http2: false);

        using var request = new HttpRequestMessage(HttpMethod.Get, Alias) { Content = new StringContent("corpo-de-teste") };
        using var rejeitada = await cliente.SendAsync(request, prazo.Token);
        Assert.Equal(HttpStatusCode.BadRequest, rejeitada.StatusCode);
        Assert.Empty(await rejeitada.Content.ReadAsByteArrayAsync(prazo.Token));

        using var readiness = await cliente.GetAsync(Readiness, prazo.Token);
        Assert.Equal(HttpStatusCode.TooManyRequests, readiness.StatusCode);
        Assert.NotNull(readiness.Headers.RetryAfter);

        using var liveness = await SondarAsync(cliente, Liveness, HttpMethod.Get, tamanhoZero: false, prazo.Token);
        using var livenessForaDaCota = await cliente.GetAsync(Liveness, prazo.Token);
        Assert.Equal(HttpStatusCode.TooManyRequests, livenessForaDaCota.StatusCode);
        Assert.NotNull(livenessForaDaCota.Headers.RetryAfter);
        Assert.Equal(0, Volatile.Read(ref consultas));
    }

    private WebApplication CriarAplicacao(bool http2, OpcoesDeLimites limites, Action consultarSql)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Configuration["AllowedHosts"] = "127.0.0.1";
        var encaminhamento = TransporteHttp.Configurar(builder);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = LimitesDeCorpo.Padrao;
            options.Listen(IPAddress.Loopback, 0, endpoint => endpoint.Protocols = http2 ? HttpProtocols.Http2 : HttpProtocols.Http1);
        });
        builder.Services.AdicionarLimitesHttp(limites);
        builder.Services.AdicionarSondasDeSaude(fixture.Database.Configuration["ConnectionStrings:DefaultConnection"]!);
        builder.Services.PostConfigure<HealthCheckServiceOptions>(options =>
        {
            var sql = Assert.Single(options.Registrations);
            options.Registrations.Remove(sql);
            options.Registrations.Add(new HealthCheckRegistration(sql.Name,
                provider => new SondaObservada(sql.Factory(provider), consultarSql), sql.FailureStatus, sql.Tags, sql.Timeout));
        });

        var app = builder.Build();
        app.UsarTransporteSeguro(encaminhamento);
        app.UseRouting();
        app.Use((context, next) =>
        {
            context.Response.Headers[CabecalhoDeConexao] = context.Connection.Id;
            return next(context);
        });
        app.UseRateLimiter();
        app.Use(LimitesHttp.LimitarFrequenciaAsync);
        app.Use(LimitesDeCorpo.VerificarAsync);
        app.MapearSondasDeSaude();
        return app;
    }

    private static HttpClient CriarCliente(WebApplication app, bool http2) => new(new SocketsHttpHandler
    {
        // O prazo do teste expira antes de o cliente desistir de esperar pelo 100 Continue.
        Expect100ContinueTimeout = TimeSpan.FromSeconds(30),
        MaxConnectionsPerServer = 1,
        UseProxy = false
    })
    {
        BaseAddress = new Uri(Assert.Single(app.Urls)),
        DefaultRequestVersion = http2 ? HttpVersion.Version20 : HttpVersion.Version11,
        DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
    };

    private static async Task<HttpResponseMessage> EnviarCorpoAsync(HttpClient cliente, string rota, HttpMethod metodo,
        int? tamanho, bool http2, CancellationToken cancellationToken)
    {
        using var conteudo = new CorpoObservado(tamanho);
        using var request = new HttpRequestMessage(metodo, rota)
        {
            Content = conteudo,
            Version = cliente.DefaultRequestVersion,
            VersionPolicy = cliente.DefaultVersionPolicy
        };
        request.Headers.ExpectContinue = true;
        if (tamanho is null && !http2)
            request.Headers.TransferEncodingChunked = true;

        var resposta = await cliente.SendAsync(request, cancellationToken);
        Assert.False(conteudo.EnvioIniciado.Task.IsCompleted);
        Assert.Equal(request.Version, resposta.Version);
        return resposta;
    }

    private static async Task<HttpResponseMessage> SondarAsync(HttpClient cliente, string rota, HttpMethod metodo,
        bool tamanhoZero, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(metodo, rota)
        {
            Content = tamanhoZero ? new ByteArrayContent([]) : null,
            Version = cliente.DefaultRequestVersion,
            VersionPolicy = cliente.DefaultVersionPolicy
        };
        var resposta = await cliente.SendAsync(request, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.True(resposta.Headers.CacheControl?.NoStore);
        var corpo = await resposta.Content.ReadAsStringAsync(cancellationToken);
        if (metodo == HttpMethod.Head)
        {
            Assert.Empty(corpo);
        }
        else
        {
            using var json = JsonDocument.Parse(corpo);
            var status = Assert.Single(json.RootElement.EnumerateObject());
            Assert.Equal("status", status.Name);
            Assert.Equal("Healthy", status.Value.GetString());
        }
        return resposta;
    }

    private static void VerificarConexoes(HttpResponseMessage rejeitada, HttpResponseMessage proxima, bool http2)
    {
        var anterior = Assert.Single(rejeitada.Headers.GetValues(CabecalhoDeConexao));
        var seguinte = Assert.Single(proxima.Headers.GetValues(CabecalhoDeConexao));
        if (http2)
        {
            Assert.False(rejeitada.Headers.Contains("Connection"));
            Assert.Equal(anterior, seguinte);
        }
        else
        {
            Assert.True(rejeitada.Headers.ConnectionClose);
            Assert.NotEqual(anterior, seguinte);
        }
    }

    private sealed class SondaObservada(IHealthCheck sql, Action consultarSql) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
        {
            consultarSql();
            return sql.CheckHealthAsync(context, cancellationToken);
        }
    }

    private sealed class CorpoObservado(int? tamanho) : HttpContent
    {
        public TaskCompletionSource EnvioIniciado { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override bool TryComputeLength(out long length)
        {
            length = tamanho.GetValueOrDefault();
            return tamanho.HasValue;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            EnvioIniciado.TrySetResult();
            return stream.WriteAsync(new byte[tamanho ?? 1].AsMemory()).AsTask();
        }
    }
}
