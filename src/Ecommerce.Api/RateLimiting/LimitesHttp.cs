using System.IdentityModel.Tokens.Jwt;
using System.Threading.RateLimiting;
using Ecommerce.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace Ecommerce.Api.RateLimiting;

internal static class LimitesHttp
{
    public const string Login = "Login";
    public static readonly object MarcadorDeSaude = new Marcador();

    public static void AdicionarLimitesHttp(this IServiceCollection services, OpcoesDeLimites opcoes)
    {
        services.AddSingleton(opcoes);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<CotasDeRequisicao>();
        services.AddScoped<LimiteDeIdentificadorLoginFilter>();
        services.AddSingleton(_ => PartitionedRateLimiter.Create<HttpContext, bool>(context =>
                RateLimitPartition.GetConcurrencyLimiter(EhSaude(context), saude => new ConcurrencyLimiterOptions
                {
                    PermitLimit = saude ? opcoes.SaudeConcorrente : opcoes.GlobalConcorrente,
                    QueueLimit = 0
                })));

        services.AddRateLimiter(options =>
        {
            options.AddConcurrencyLimiter(Login, limiter =>
            {
                limiter.PermitLimit = opcoes.LoginConcorrente;
                limiter.QueueLimit = 0;
            });
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) => new ValueTask(RespostaDeLimite.EscreverAsync(context.HttpContext, null));
        });
        services.AddOptions<RateLimiterOptions>().Configure<PartitionedRateLimiter<HttpContext>>(
            (options, concorrencia) => options.GlobalLimiter = concorrencia);
    }

    public static Task LimitarFrequenciaAsync(HttpContext context, RequestDelegate next)
    {
        var cotas = context.RequestServices.GetRequiredService<CotasDeRequisicao>();
        if (context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName == Login &&
            cotas.ConsumirOrigem(context.Connection.RemoteIpAddress) is { } esperaPorOrigem)
        {
            return RespostaDeLimite.EscreverAsync(context, esperaPorOrigem);
        }

        return cotas.ConsumirOrcamento(EhSaude(context)) is { } espera
            ? RespostaDeLimite.EscreverAsync(context, espera)
            : next(context);
    }

    public static Task LimitarUsuarioAsync(HttpContext context, RequestDelegate next)
    {
        var metadados = context.GetEndpoint()?.Metadata;
        if (metadados is null || metadados.GetMetadata<IAllowAnonymous>() is not null ||
            context.User.Identity?.IsAuthenticated != true ||
            !Guid.TryParseExact(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, "D", out var usuario))
        {
            return next(context);
        }

        var autorizacoes = metadados.GetOrderedMetadata<IAuthorizeData>();
        var cotas = context.RequestServices.GetRequiredService<CotasDeRequisicao>();
        var espera = autorizacoes.Any(autorizacao => autorizacao.Policy == PoliticasDeAcesso.Provisionamento)
            ? cotas.ConsumirCadastro(usuario)
            : autorizacoes.Any(autorizacao => autorizacao.Policy == PoliticasDeAcesso.Operacao)
                ? cotas.ConsumirOperacao(usuario)
                : null;

        return espera is null ? next(context) : RespostaDeLimite.EscreverAsync(context, espera);
    }

    private static bool EhSaude(HttpContext context) => context.GetEndpoint()?.Metadata.GetMetadata<Marcador>() is not null;

    private sealed class Marcador;
}
