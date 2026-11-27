using System.Text;
using Ecommerce.Api.Middlewares;
using Ecommerce.Application.Exceptions;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

namespace Ecommerce.Tests;

public sealed class ExceptionMiddlewareTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RespostaIniciada_AbortaSemAlterarStatusCabecalhosOuCorpo(bool falhaDeNegocio)
    {
        using var logs = new LogsObservaveis();
        using var logger = LoggerFactory.Create(config => config.AddProvider(logs));
        using var corpo = new MemoryStream();
        await corpo.WriteAsync(Encoding.UTF8.GetBytes("resposta parcial"));
        var resposta = new RespostaIniciada { StatusCode = 202 };
        resposta.Headers.ContentType = "text/plain";
        var conexao = new Conexao();
        var context = new DefaultHttpContext { TraceIdentifier = "rastreio-de-teste" };
        context.Features.Set<IHttpResponseFeature>(resposta);
        context.Features.Set<IHttpRequestLifetimeFeature>(conexao);
        context.Response.Body = corpo;

        const string entradaPrivada = "não registrar esta entrada";
        var middleware = new ExceptionMiddleware(
            _ => throw (falhaDeNegocio ? new CredenciaisInvalidasException() : new InvalidOperationException(entradaPrivada)),
            logger.CreateLogger<ExceptionMiddleware>());
        await middleware.InvokeAsync(context);

        Assert.True(conexao.Abortada);
        Assert.Equal(202, context.Response.StatusCode);
        Assert.Equal("text/plain", context.Response.ContentType);
        Assert.False(context.Response.Headers.ContainsKey("WWW-Authenticate"));
        Assert.Equal("resposta parcial", Encoding.UTF8.GetString(corpo.ToArray()));
        var falha = Assert.Single(logs.Registros);
        Assert.Equal(LogLevel.Error, falha.Nivel);
        Assert.Null(falha.Excecao);
        Assert.Contains("rastreio-de-teste", falha.Mensagem);
        Assert.DoesNotContain(entradaPrivada, falha.Mensagem);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DesconexaoDoCliente_NaoGeraNemRegistraErroInterno(bool falhaDeLeitura)
    {
        using var logs = new LogsObservaveis();
        using var logger = LoggerFactory.Create(config => config.AddProvider(logs));
        using var cancelamento = new CancellationTokenSource();
        using var corpo = new MemoryStream();
        var context = new DefaultHttpContext { RequestAborted = cancelamento.Token };
        context.Response.Body = corpo;
        var middleware = new ExceptionMiddleware(_ =>
        {
            cancelamento.Cancel();
            throw falhaDeLeitura ? new ConnectionResetException("leitura interrompida") : new OperationCanceledException(cancelamento.Token);
        }, logger.CreateLogger<ExceptionMiddleware>());

        await middleware.InvokeAsync(context);

        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal(0, corpo.Length);
        Assert.Empty(logs.Registros);
    }

    [Fact]
    public async Task CancelamentoInterno_ComConexaoAtivaRecebe500Generico()
    {
        using var corpo = new MemoryStream();
        var context = new DefaultHttpContext();
        context.Response.Body = corpo;
        var middleware = new ExceptionMiddleware(_ => throw new OperationCanceledException("detalhe interno"),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(500, context.Response.StatusCode);
        Assert.DoesNotContain("detalhe interno", Encoding.UTF8.GetString(corpo.ToArray()));
    }

    [Fact]
    public async Task CancelamentoInterno_ContinuaSendoFalhaQuandoOClienteTambemDesconecta()
    {
        using var logs = new LogsObservaveis();
        using var logger = LoggerFactory.Create(config => config.AddProvider(logs));
        using var cliente = new CancellationTokenSource();
        using var prazoInterno = new CancellationTokenSource();
        using var corpo = new MemoryStream();
        var context = new DefaultHttpContext { RequestAborted = cliente.Token };
        context.Response.Body = corpo;
        var middleware = new ExceptionMiddleware(_ =>
        {
            prazoInterno.Cancel();
            cliente.Cancel();
            throw new OperationCanceledException("falha interna", prazoInterno.Token);
        }, logger.CreateLogger<ExceptionMiddleware>());

        await middleware.InvokeAsync(context);

        Assert.Equal(0, corpo.Length);
        var falha = Assert.Single(logs.Registros);
        Assert.Equal(LogLevel.Error, falha.Nivel);
        Assert.Contains(nameof(OperationCanceledException), falha.Mensagem);
        Assert.Null(falha.Excecao);
    }

    [Fact]
    public async Task FalhaAoEscreverErro_AbortaSemEncaminharExcecaoAoHost()
    {
        using var logs = new LogsObservaveis();
        using var logger = LoggerFactory.Create(config => config.AddProvider(logs));
        using var corpo = new CorpoComFalha();
        var conexao = new Conexao();
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpRequestLifetimeFeature>(conexao);
        context.Response.Body = corpo;
        var middleware = new ExceptionMiddleware(_ => throw new InvalidOperationException("dado interno"),
            logger.CreateLogger<ExceptionMiddleware>());

        await middleware.InvokeAsync(context);

        Assert.True(conexao.Abortada);
        Assert.Equal(2, logs.Registros.Count);
        Assert.All(logs.Registros, registro =>
        {
            Assert.Null(registro.Excecao);
            Assert.DoesNotContain("dado interno", registro.Mensagem);
        });
    }

    private sealed class CorpoComFalha : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException(new IOException("dado interno"));
    }

    private sealed class RespostaIniciada : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }

    private sealed class Conexao : IHttpRequestLifetimeFeature
    {
        public bool Abortada { get; private set; }
        public CancellationToken RequestAborted { get; set; }
        public void Abort() => Abortada = true;
    }
}
