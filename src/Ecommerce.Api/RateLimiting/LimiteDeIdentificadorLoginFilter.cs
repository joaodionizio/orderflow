using Ecommerce.Application.Dtos.Requests;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Ecommerce.Api.RateLimiting;

internal sealed class LimiteDeIdentificadorLoginFilter(CotasDeRequisicao cotas) : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ActionArguments.Values.OfType<LoginRequest>().FirstOrDefault() is { } request &&
            cotas.ConsumirIdentificador(request.Email) is { } espera)
        {
            context.Result = new RespostaDeLimite(espera);
        }
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
