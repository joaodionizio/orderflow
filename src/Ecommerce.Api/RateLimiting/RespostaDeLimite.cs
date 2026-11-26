using System.Globalization;
using Ecommerce.Api.Responses;
using Microsoft.AspNetCore.Mvc;

namespace Ecommerce.Api.RateLimiting;

internal sealed class RespostaDeLimite(TimeSpan? espera) : IActionResult
{
    private static readonly MensagemDeErroResponse Corpo = new("Muitas requisições. Tente novamente mais tarde.");

    public Task ExecuteResultAsync(ActionContext context) => EscreverAsync(context.HttpContext, espera);

    public static Task EscreverAsync(HttpContext context, TimeSpan? espera)
    {
        var response = context.Response;
        response.StatusCode = StatusCodes.Status429TooManyRequests;
        response.Headers.CacheControl = "no-store";

        if (espera is { } intervalo)
            response.Headers.RetryAfter = Math.Max(1, Math.Ceiling(intervalo.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        response.ContentType = "application/json; charset=utf-8";
        return HttpMethods.IsHead(context.Request.Method)
            ? Task.CompletedTask
            : response.WriteAsJsonAsync(Corpo, cancellationToken: context.RequestAborted);
    }
}
