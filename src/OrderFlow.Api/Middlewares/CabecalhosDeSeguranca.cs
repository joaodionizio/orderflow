using Microsoft.Net.Http.Headers;

namespace OrderFlow.Api.Middlewares;

internal sealed class CabecalhosDeSeguranca : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(AdicionarAsync);
        next(app);
    };

    private static Task AdicionarAsync(HttpContext context, RequestDelegate next)
    {
        context.Response.OnStarting(static state =>
        {
            var resposta = (HttpResponse)state;
            var cabecalhos = resposta.Headers;
            cabecalhos[HeaderNames.XContentTypeOptions] = "nosniff";
            cabecalhos["Referrer-Policy"] = "no-referrer";

            var tipo = resposta.ContentType.AsSpan();
            var separador = tipo.IndexOf(';');
            if ((separador < 0 ? tipo : tipo[..separador]).Trim().Equals("text/html", StringComparison.OrdinalIgnoreCase))
                cabecalhos[HeaderNames.XFrameOptions] = "DENY";

            if (resposta.StatusCode == StatusCodes.Status307TemporaryRedirect && cabecalhos.ContainsKey(HeaderNames.Location))
                cabecalhos.CacheControl = "no-store";

            return Task.CompletedTask;
        }, context.Response);

        return next(context);
    }
}
