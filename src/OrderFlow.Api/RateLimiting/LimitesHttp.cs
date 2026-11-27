using System.IdentityModel.Tokens.Jwt;
using System.Threading.RateLimiting;
using OrderFlow.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace OrderFlow.Api.RateLimiting;

internal static class LimitesHttp
{
    public const string Login = "Login";
    public static readonly object CotaDeReadiness = new Marcador(OrcamentoHttp.Readiness);
    public static readonly object CotaDeLiveness = new Marcador(OrcamentoHttp.Liveness);

    public static void AdicionarLimitesHttp(this IServiceCollection services, OpcoesDeLimites opcoes)
    {
        services.AddSingleton(opcoes);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<CotasDeRequisicao>();
        services.AddScoped<LimiteDeIdentificadorLoginFilter>();
        services.AddSingleton(_ => PartitionedRateLimiter.Create<HttpContext, OrcamentoHttp>(context =>
                RateLimitPartition.GetConcurrencyLimiter(ObterOrcamento(context), orcamento => new ConcurrencyLimiterOptions
                {
                    PermitLimit = orcamento switch
                    {
                        OrcamentoHttp.Readiness => opcoes.SaudeConcorrente,
                        OrcamentoHttp.Liveness => opcoes.LivenessConcorrente,
                        _ => opcoes.GlobalConcorrente
                    },
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

        return cotas.ConsumirOrcamento(ObterOrcamento(context)) is { } espera
            ? RespostaDeLimite.EscreverAsync(context, espera)
            : next(context);
    }

    public static Task LimitarUsuarioAsync(HttpContext context, RequestDelegate next)
    {
        if (context.GetEndpoint() is not RouteEndpoint endpoint || endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null ||
            context.User.Identity?.IsAuthenticated != true ||
            !Guid.TryParseExact(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, "D", out var usuario))
        {
            return next(context);
        }

        var autorizacoes = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
        var cotas = context.RequestServices.GetRequiredService<CotasDeRequisicao>();
        var espera = autorizacoes.Any(autorizacao => autorizacao.Policy == PoliticasDeAcesso.Provisionamento)
            ? cotas.ConsumirCadastro(usuario)
            : cotas.ConsumirOperacao(usuario);

        return espera is null ? next(context) : RespostaDeLimite.EscreverAsync(context, espera);
    }

    private static OrcamentoHttp ObterOrcamento(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<Marcador>()?.Orcamento ?? OrcamentoHttp.Api;

    private sealed record Marcador(OrcamentoHttp Orcamento);
}

internal enum OrcamentoHttp { Api, Readiness, Liveness }
