using System.Text.Json;
using Ecommerce.Api.Responses;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Ecommerce.Api.Middlewares;

internal sealed class LimitesDeCorpo : IAsyncAlwaysRunResultFilter
{
    public const int Padrao = 256 * 1024;
    public const int Login = 16 * 1024;
    public const int ProfundidadeJson = 32;

    private static readonly byte[] CorpoDeExcesso = JsonSerializer.SerializeToUtf8Bytes(
        new MensagemDeErroResponse("O corpo da requisição excede o tamanho permitido."));

    public static async Task VerificarAsync(HttpContext context, RequestDelegate next)
    {
        var metadados = context.GetEndpoint()?.Metadata;
        var limite = metadados?.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize ?? Padrao;
        if (context.Request.ContentLength > limite)
        {
            await ResponderExcessoAsync(context);
            return;
        }

        // Sem binding do corpo, o Kestrel drenaria a requisição só depois da resposta.
        var acao = metadados?.GetMetadata<ControllerActionDescriptor>();
        if (metadados is not null && PodeTerCorpo(context)
            && acao?.Parameters.Any(parametro => parametro.BindingInfo?.BindingSource == BindingSource.Body) != true)
        {
            await DescartarCorpoAsync(context);
        }

        await next(context);
    }

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        // O MVC pode rejeitar o formato ou a sintaxe antes de ler todo o corpo.
        // Sem tamanho declarado, deixe o Kestrel limitar também os bytes restantes.
        var http = context.HttpContext;
        if (http.Request.ContentLength is null && PodeTerCorpo(http)
            && context.Result is IStatusCodeActionResult
            {
                StatusCode: StatusCodes.Status400BadRequest or StatusCodes.Status415UnsupportedMediaType
            })
        {
            await DescartarCorpoAsync(http);
        }

        await next();
    }

    public static Task ResponderExcessoAsync(HttpContext context)
    {
        var response = context.Response;
        response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        response.ContentType = "application/json";
        // Ao rejeitar o corpo, o Kestrel pode encerrar a conexão HTTP/1 antes de reutilizá-la.
        if (HttpProtocol.IsHttp10(context.Request.Protocol) || HttpProtocol.IsHttp11(context.Request.Protocol))
            response.Headers.Connection = "close";

        response.ContentLength = CorpoDeExcesso.Length;
        return response.Body.WriteAsync(CorpoDeExcesso, context.RequestAborted).AsTask();
    }

    private static bool PodeTerCorpo(HttpContext context) =>
        context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true;

    private static Task DescartarCorpoAsync(HttpContext context) =>
        context.Request.Body.CopyToAsync(Stream.Null, context.RequestAborted);
}
