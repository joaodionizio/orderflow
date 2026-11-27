using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Ecommerce.Api.Middlewares;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Ecommerce.Tests;

public sealed class TransporteHttpTests
{
    private const string HostPermitido = "orderflow.example.test";

    [Theory]
    [InlineData("AllowedHosts", null)]
    [InlineData("AllowedHosts", "  ")]
    [InlineData("AllowedHosts", "orderflow.example.test;*")]
    [InlineData("AllowedHosts", "*.example.test")]
    [InlineData("AllowedHosts", "https://orderflow.example.test")]
    [InlineData("AllowedHosts", "orderflow.example.test:443")]
    [InlineData("AllowedHosts", "orderflow.example.test/path")]
    [InlineData("AllowedHosts", "0.0.0.0")]
    [InlineData("AllowedHosts", "[::]")]
    [InlineData("ForwardedHeaders_Enabled", "true")]
    [InlineData("TrustedProxies:Enabled", "talvez")]
    [InlineData("TrustedProxies:Enabled", "true")]
    [InlineData("TrustedProxies:Addresses", "192.0.2.5")]
    [InlineData("TrustedProxies:Addresses:0", "proxy.example.test")]
    [InlineData("TrustedProxies:Addresses:0", "::ffff:0.0.0.0")]
    [InlineData("TrustedProxies:Networks:0", "192.0.2.5/24")]
    [InlineData("TrustedProxies:Networks:0", "2001:db8::/129")]
    [InlineData("TrustedProxies:Networks:0", "0.0.0.0/0")]
    [InlineData("TrustedProxies:Networks:0", "::/0")]
    [InlineData("TrustedProxies:Networks:0", "::ffff:0.0.0.0/96")]
    [InlineData("TrustedProxies:Networks:0", "::/80")]
    [InlineData("TrustedProxies:Networks:0", "::fffe:0:0/95")]
    public void StartupRecusaConfiguracaoInvalidaAntesDeAcessarBanco(string chave, string? valor)
    {
        var configuracao = new Dictionary<string, string?> { ["AllowedHosts"] = HostPermitido, [chave] = valor };
        if (chave.StartsWith("TrustedProxies:", StringComparison.Ordinal) && chave != "TrustedProxies:Enabled")
            configuracao["TrustedProxies:Enabled"] = "true";

        using var raiz = new AutorizacaoApiFixture.ApiFactory(configuracao);
        using var api = raiz.WithWebHostBuilder(builder => builder.UseEnvironment(Environments.Production));
        var erro = Assert.Throws<InvalidOperationException>(() => api.CreateClient());
        var campo = chave == "ForwardedHeaders_Enabled" ? "ASPNETCORE_FORWARDEDHEADERS_ENABLED" : chave;
        if (chave == "TrustedProxies:Enabled" && valor == "true")
            campo = "TrustedProxies";
        Assert.Contains(campo, erro.Message);
    }

    [Theory]
    [InlineData("Networks", "2001:db8:1::/64", "2001:db8:1::5", true)]
    [InlineData("Networks", "2001:db8:1::/64", "2001:db8:2::5", false)]
    [InlineData("Networks", "2001:db8:1::/64", "::1", false)]
    [InlineData("Networks", "2001:db8:1::/64", null, false)]
    [InlineData("Networks", "::ffff:192.0.2.0/120", "192.0.2.5", true)]
    [InlineData("Networks", "192.0.2.0/24", "::ffff:192.0.2.5", true)]
    [InlineData("Addresses", "::ffff:192.0.2.5", "192.0.2.5", true)]
    [InlineData("Addresses", "2001:db8:1::5", "2001:db8:1::5", true)]
    public async Task EncaminhamentoConfiaApenasNoIpDaConexao(string lista, string entrada, string? remoto, bool confiavel)
    {
        var builder = CriarBuilder(Environments.Production);
        builder.WebHost.UseTestServer();
        builder.Configuration[$"TrustedProxies:{lista}:0"] = entrada;
        builder.Configuration["TrustedProxies:Enabled"] = "true";
        var opcoes = TransporteHttp.Configurar(builder);
        await using var app = builder.Build();
        app.UsarTransporteSeguro(opcoes);
        app.MapGet("/", (HttpContext context) => context.Connection.RemoteIpAddress?.ToString());
        await app.StartAsync();

        var context = await app.GetTestServer().SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = remoto is null ? null : IPAddress.Parse(remoto);
            context.Request.Method = HttpMethods.Get;
            context.Request.Scheme = "http";
            context.Request.Path = "/";
            context.Request.Host = new HostString(HostPermitido);
            context.Request.Headers["X-Forwarded-For"] = "198.51.100.10";
            context.Request.Headers["X-Forwarded-Proto"] = "https";
            context.Request.Headers["X-Forwarded-Host"] = "outro.example.test";
        });

        Assert.Equal(confiavel ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal(confiavel ? IPAddress.Parse("198.51.100.10") : remoto is null ? null : IPAddress.Parse(remoto),
            context.Connection.RemoteIpAddress);
        Assert.Equal(HostPermitido, context.Request.Host.Value);
    }

    [Fact]
    public async Task RejeitaParesInvalidosOuDuplicadosSemAlterarIpOuEsquema()
    {
        const string ipCliente = "198.51.100.10";
        const string outroIp = "198.51.100.11";
        var ipProxy = IPAddress.Parse("192.0.2.5");
        var builder = CriarBuilder(Environments.Production);
        builder.WebHost.UseTestServer();
        builder.Configuration["TrustedProxies:Enabled"] = "true";
        builder.Configuration["TrustedProxies:Addresses:0"] = ipProxy.ToString();
        var opcoes = TransporteHttp.Configurar(builder);
        await using var app = builder.Build();
        app.UsarTransporteSeguro(opcoes);
        app.MapGet("/", () => Results.Ok());
        await app.StartAsync();

        (string[]? Enderecos, string[]? Esquemas)[] paresInvalidos =
        [
            (null, ["https"]),
            ([ipCliente], null),
            (["ip-invalido"], ["https"]),
            ([$"\"{ipCliente}\""], ["https"]),
            ([""], ["https"]),
            ([ipCliente], ["ftp"]),
            ([ipCliente], ["https", "https"]),
            ([ipCliente, outroIp], ["http", "https"]),
            ([$"{ipCliente}, {outroIp}"], ["http, https"])
        ];

        foreach (var (enderecos, esquemas) in paresInvalidos)
        {
            foreach (var esquema in new[] { "http", "https" })
            {
                var context = await app.GetTestServer().SendAsync(context =>
                {
                    context.Connection.RemoteIpAddress = ipProxy;
                    context.Request.Method = HttpMethods.Get;
                    context.Request.Scheme = esquema;
                    context.Request.Path = "/";
                    context.Request.Host = new HostString(HostPermitido);
                    context.Request.Headers["X-Forwarded-For"] = enderecos;
                    context.Request.Headers["X-Forwarded-Proto"] = esquemas;
                });

                Assert.Equal(ipProxy, context.Connection.RemoteIpAddress);
                Assert.Equal(esquema, context.Request.Scheme);
                Assert.Equal(esquema == "https" ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest,
                    context.Response.StatusCode);
            }
        }
    }

    [Fact]
    public async Task EncaminhamentoAutomaticoContinuaInerteQuandoFlagMudaAposValidacao()
    {
        var builder = CriarBuilder(Environments.Production);
        builder.WebHost.UseTestServer();
        var opcoes = TransporteHttp.Configurar(builder);
        builder.Configuration["ForwardedHeaders_Enabled"] = "true";
        await using var app = builder.Build();
        app.UsarTransporteSeguro(opcoes);
        app.MapGet("/", () => Results.Ok());
        await app.StartAsync();

        var ip = IPAddress.Parse("198.51.100.10");
        var context = await app.GetTestServer().SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = ip;
            context.Request.Method = HttpMethods.Get;
            context.Request.Scheme = "http";
            context.Request.Path = "/";
            context.Request.Host = new HostString(HostPermitido);
            context.Request.Headers["X-Forwarded-For"] = "198.51.100.11";
            context.Request.Headers["X-Forwarded-Proto"] = "https";
        });

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal(ip, context.Connection.RemoteIpAddress);
        Assert.Equal("http", context.Request.Scheme);
    }

    [Fact]
    public async Task FiltroDeHostsComparaNomesEIpsSemDistinguirMaiusculasOuPorta()
    {
        var builder = CriarBuilder(Environments.Production);
        builder.WebHost.UseTestServer();
        builder.Configuration["AllowedHosts"] = $"{HostPermitido};192.0.2.5;[2001:db8::5]";
        var opcoes = TransporteHttp.Configurar(builder);
        await using var app = builder.Build();
        app.UsarTransporteSeguro(opcoes);
        app.MapGet("/", () => Results.Ok());
        await app.StartAsync();

        foreach (var (host, permitido) in new[]
        {
            ("ORDERFLOW.EXAMPLE.TEST:8443", true), ("192.0.2.5", true), ("[2001:DB8::5]:8443", true),
            ("app.orderflow.example.test", false), ("192.0.2.6", false), ("[2001:db8::6]:8443", false), ("", false)
        })
        {
            var context = await app.GetTestServer().SendAsync(context =>
            {
                context.Request.Method = HttpMethods.Get;
                context.Request.Scheme = "https";
                context.Request.Path = "/";
                context.Request.Host = new HostString(host);
            });

            Assert.Equal(permitido ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest, context.Response.StatusCode);
        }
    }

    [Fact]
    public async Task KestrelEmDevelopmentRedirecionaParaPortaTlsSemHsts()
    {
        using var certificado = CriarCertificado();
        var builder = CriarBuilder(Environments.Development);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, 0);
            options.Listen(IPAddress.Loopback, 0, listener => listener.UseHttps(certificado));
        });
        var opcoes = TransporteHttp.Configurar(builder);
        await using var app = builder.Build();
        app.UsarTransporteSeguro(opcoes);
        app.MapPost("/", () => Results.Ok());
        await app.StartAsync();

        using var cliente = CriarCliente(certificado);
        var http = new Uri(app.Urls.Single(url => url.StartsWith("http://", StringComparison.Ordinal)));
        var https = new Uri(app.Urls.Single(url => url.StartsWith("https://", StringComparison.Ordinal)));
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(http, "/?pagina=2"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "token-local-de-teste");
        request.Headers.Add("X-Forwarded-Proto", "https");
        using var redirecionamento = await cliente.SendAsync(request);
        Assert.Equal(HttpStatusCode.TemporaryRedirect, redirecionamento.StatusCode);
        Assert.Equal(new Uri(https, "/?pagina=2"), redirecionamento.Headers.Location);
        Assert.True(redirecionamento.Headers.CacheControl?.NoStore);
        VerificarCabecalhos(redirecionamento);

        using var segura = await cliente.PostAsync(https, content: null);
        Assert.Equal(HttpStatusCode.OK, segura.StatusCode);
        VerificarCabecalhos(segura);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KestrelEmProductionAceitaTlsDiretoOuEncaminhadoSemRedirecionar(bool usarProxy)
    {
        using var certificado = CriarCertificado();
        var builder = CriarBuilder(Environments.Production);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, 0);
            options.Listen(IPAddress.Loopback, 0, listener => listener.UseHttps(certificado));
        });
        builder.Configuration["TrustedProxies:Enabled"] = usarProxy ? "true" : "false";
        builder.Configuration["TrustedProxies:Addresses:0"] = "127.0.0.1";
        var opcoes = TransporteHttp.Configurar(builder);
        await using var app = builder.Build();
        app.UsarTransporteSeguro(opcoes);
        app.MapGet("/", () => Results.Ok());
        await app.StartAsync();

        using var cliente = CriarCliente(certificado);
        foreach (var url in app.Urls)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Host = HostPermitido;
            request.Headers.Add("Origin", "https://outro.example.test");
            var direto = request.RequestUri!.Scheme == "https";
            if (!direto)
            {
                request.Headers.Add("X-Forwarded-For", "198.51.100.10");
                request.Headers.Add("X-Forwarded-Proto", "https");
            }

            using var response = await cliente.SendAsync(request);
            var segura = direto || usarProxy;
            Assert.Equal(segura ? HttpStatusCode.OK : HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Null(response.Headers.Location);
            Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
            VerificarCabecalhos(response, hsts: segura);
        }
    }

    internal static void VerificarCabecalhos(HttpResponseMessage response, bool hsts = false, bool html = false)
    {
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Empty(response.Headers.Server);
        if (html)
            Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        else
            Assert.False(response.Headers.Contains("X-Frame-Options"));
        if (hsts)
            Assert.Equal("max-age=2592000", Assert.Single(response.Headers.GetValues("Strict-Transport-Security")));
        else
            Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    private static WebApplicationBuilder CriarBuilder(string ambiente)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = ambiente });
        builder.Configuration["AllowedHosts"] = ambiente == Environments.Development ? "127.0.0.1" : HostPermitido;
        return builder;
    }

    private static X509Certificate2 CriarCertificado()
    {
        using var chave = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", chave, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
    }

    private static HttpClient CriarCliente(X509Certificate2 certificado) => new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        ServerCertificateCustomValidationCallback = (_, recebido, _, _) =>
            recebido is not null && recebido.Thumbprint == certificado.Thumbprint
    });
}
