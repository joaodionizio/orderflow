using System.Text.Json;
using Ecommerce.Api.Controllers;
using Ecommerce.Api.Responses;
using Ecommerce.Application.Exceptions;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Data.SqlClient;

namespace Ecommerce.Api.Middlewares;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(
        RequestDelegate next,
        ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var acao = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        var autenticacao = acao?.ControllerTypeInfo.AsType() == typeof(AuthController);
        if (autenticacao)
            context.Response.Headers.CacheControl = "no-store";

        try
        {
            await _next(context);
        }
        catch (Exception ex) when (ConexaoEncerrada(context, ex))
        {
            // O cliente já encerrou a conexão.
        }
        catch (Exception ex)
        {
            try
            {
                await TratarFalhaAsync(context, acao, ex);
            }
            catch (Exception falhaDeEscrita) when (ConexaoEncerrada(context, falhaDeEscrita))
            {
                // A conexão foi encerrada durante o envio da resposta de erro.
            }
            catch (Exception falhaDeEscrita)
            {
                RegistrarFalha(context, acao, falhaDeEscrita);
                context.Abort();
            }
        }
    }

    private Task TratarFalhaAsync(HttpContext context, ControllerActionDescriptor? acao, Exception ex)
    {
        if (!context.Response.HasStarted && !context.RequestAborted.IsCancellationRequested)
        {
            switch (ex)
            {
                case EntidadeNaoEncontradaException:
                    return EscreverRespostaAsync(context, StatusCodes.Status404NotFound, ex.Message);
                case CredenciaisInvalidasException:
                    context.Response.Headers.WWWAuthenticate = AuthController.DesafioLogin;
                    return EscreverRespostaAsync(context, StatusCodes.Status401Unauthorized, CredenciaisInvalidasException.MensagemPublica);
                case EmailDeUsuarioEmUsoException:
                    _logger.LogInformation("Conflito de unicidade na operação {Operacao}. TraceId: {TraceId}",
                        nameof(AuthController.Registrar), context.TraceIdentifier);
                    return EscreverRespostaAsync(context, StatusCodes.Status409Conflict, EmailDeUsuarioEmUsoException.MensagemPublica);
                case RegraDeNegocioException:
                    return EscreverRespostaAsync(context, StatusCodes.Status400BadRequest, ex.Message);
                case BadHttpRequestException http:
                    return http.StatusCode == StatusCodes.Status413PayloadTooLarge
                        ? LimitesDeCorpo.ResponderExcessoAsync(context)
                        : EscreverRespostaAsync(context, http.StatusCode, "A requisição HTTP é inválida.");
            }
        }

        RegistrarFalha(context, acao, ex);
        if (context.Response.HasStarted)
        {
            context.Abort();
            return Task.CompletedTask;
        }

        if (context.RequestAborted.IsCancellationRequested)
            return Task.CompletedTask;

        var hsts = context.Response.Headers.StrictTransportSecurity;
        context.Response.Clear();
        if (hsts.Count > 0)
            context.Response.Headers.StrictTransportSecurity = hsts;
        context.Response.Headers.CacheControl = "no-store";
        return EscreverRespostaAsync(context, StatusCodes.Status500InternalServerError, "Ocorreu um erro interno no servidor.");
    }

    private void RegistrarFalha(HttpContext context, ControllerActionDescriptor? acao, Exception ex)
    {
        var operacao = acao is not null
            ? $"{acao.ControllerName}.{acao.ActionName}"
            : (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
        var causa = ex.GetBaseException();
        _logger.LogError(
            "Erro inesperado em {Operacao}. Tipo: {TipoErro}. Causa: {Causa}. SQL: {CodigoSql}. TraceId: {TraceId}. Resposta iniciada: {RespostaIniciada}",
            operacao, ex.GetType().Name, causa.GetType().Name, ObterCodigoSql(ex),
            context.TraceIdentifier, context.Response.HasStarted);
    }

    private static int? ObterCodigoSql(Exception ex)
    {
        for (Exception? falha = ex; falha is not null; falha = falha.InnerException)
        {
            if (falha is SqlException sql)
                return sql.Number;
        }

        return null;
    }

    private static bool ConexaoEncerrada(HttpContext context, Exception ex) =>
        context.RequestAborted.IsCancellationRequested &&
        (ex is ConnectionAbortedException or ConnectionResetException ||
         ex is OperationCanceledException cancelamento && cancelamento.CancellationToken == context.RequestAborted);

    private static async Task EscreverRespostaAsync(
        HttpContext context,
        int statusCode,
        string mensagem)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        if (!HttpMethods.IsHead(context.Request.Method))
            await context.Response.WriteAsync(
                JsonSerializer.Serialize(new MensagemDeErroResponse(mensagem)), context.RequestAborted);
    }
}
