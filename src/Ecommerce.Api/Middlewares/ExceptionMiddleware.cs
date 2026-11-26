using System.Text.Json;
using Ecommerce.Api.Controllers;
using Ecommerce.Api.Responses;
using Ecommerce.Application.Exceptions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

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
        catch (EntidadeNaoEncontradaException ex)
        {
            await EscreverRespostaAsync(
                context,
                StatusCodes.Status404NotFound,
                ex.Message);
        }
        catch (CredenciaisInvalidasException ex)
        {
            context.Response.Headers.WWWAuthenticate = AuthController.DesafioLogin;
            await EscreverRespostaAsync(context, StatusCodes.Status401Unauthorized, ex.Message);
        }
        catch (EmailDeUsuarioEmUsoException ex)
        {
            _logger.LogInformation("Conflito de unicidade na operação {Operacao}. TraceId: {TraceId}",
                nameof(AuthController.Registrar), context.TraceIdentifier);
            await EscreverRespostaAsync(context, StatusCodes.Status409Conflict, ex.Message);
        }
        catch (RegraDeNegocioException ex)
        {
            await EscreverRespostaAsync(
                context,
                StatusCodes.Status400BadRequest,
                ex.Message);
        }
        catch (BadHttpRequestException ex) when (!context.Response.HasStarted)
        {
            if (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
                await LimitesDeCorpo.ResponderExcessoAsync(context);
            else
                await EscreverRespostaAsync(context, ex.StatusCode, "A requisição HTTP é inválida.");
        }
        catch (Exception ex)
        {
            var causa = ex.GetBaseException();
            if (autenticacao || ex is DbUpdateException || causa is SqlException)
            {
                _logger.LogError(
                    "Erro inesperado em {Controller}.{Operacao}. Tipo: {TipoErro}. Causa: {Causa}. SQL: {CodigoSql}. TraceId: {TraceId}",
                    acao?.ControllerName, acao?.ActionName, ex.GetType().Name, causa.GetType().Name,
                    (causa as SqlException)?.Number,
                    context.TraceIdentifier);
            }
            else
            {
                _logger.LogError(
                    ex,
                    "Erro inesperado ao processar {Metodo} {Caminho}. TraceId: {TraceId}",
                    context.Request.Method,
                    context.Request.Path,
                    context.TraceIdentifier);
            }

            await EscreverRespostaAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "Ocorreu um erro interno no servidor.");
        }
    }

    private static async Task EscreverRespostaAsync(
        HttpContext context,
        int statusCode,
        string mensagem)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(new MensagemDeErroResponse(mensagem)));
    }
}
