using Ecommerce.Api.RateLimiting;
using HealthChecks.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ecommerce.Api.Health;

internal static class SondasDeSaude
{
    public const string Readiness = "readiness";
    private const int PrazoEmSegundos = 3;

    public static void AdicionarSondasDeSaude(this IServiceCollection services, string connectionString)
    {
        var conexao = new SqlConnectionStringBuilder(connectionString);
        // O SqlClient pode aguardar o timeout próprio durante o pre-login, mesmo após o cancelamento.
        conexao.ConnectTimeout = conexao.ConnectTimeout == 0
            ? PrazoEmSegundos
            : Math.Min(conexao.ConnectTimeout, PrazoEmSegundos);
        conexao.ConnectRetryCount = 0;
        var sql = new SqlServerHealthCheck(new SqlServerHealthCheckOptions { ConnectionString = conexao.ConnectionString });
        // A categoria nativa pode registrar descrições, dados e exceções de qualquer verificação.
        services.AddLogging(logging => logging.AddFilter("Microsoft.Extensions.Diagnostics.HealthChecks.DefaultHealthCheckService",
            LogLevel.None));
        services.AddHealthChecks().Add(new HealthCheckRegistration(
            "sqlserver", provider => new SondaSqlServer(sql, provider.GetRequiredService<ILogger<SondaSqlServer>>()),
            failureStatus: HealthStatus.Unhealthy, tags: [Readiness]));
    }

    public static void MapearSondasDeSaude(this IEndpointRouteBuilder app)
    {
        MapearSonda(app, "/health/ready", readiness: true);
        MapearSonda(app, "/health", readiness: true);
        MapearSonda(app, "/health/live", readiness: false);
    }

    private static void MapearSonda(IEndpointRouteBuilder app, string rota, bool readiness)
    {
        var cota = readiness ? LimitesHttp.CotaDeReadiness : LimitesHttp.CotaDeLiveness;
        var get = app.MapGet(rota, SondarAsync).WithMetadata(cota, Sonda.Marcador).AllowAnonymous()
            .Produces<EstadoDaSaude>()
            .Produces(StatusCodes.Status400BadRequest);
        var head = app.MapMethods(rota, [HttpMethods.Head], SondarAsync).WithMetadata(cota, Sonda.Marcador).AllowAnonymous()
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

        if (readiness)
        {
            get.Produces<EstadoDaSaude>(StatusCodes.Status503ServiceUnavailable);
            head.Produces(StatusCodes.Status503ServiceUnavailable);
        }

        async Task SondarAsync(HttpContext context, HealthCheckService servico)
        {
            var relatorio = await servico.CheckHealthAsync(registro => readiness && registro.Tags.Contains(Readiness),
                context.RequestAborted);
            var resposta = context.Response;
            resposta.StatusCode = relatorio.Status == HealthStatus.Healthy
                ? StatusCodes.Status200OK
                : StatusCodes.Status503ServiceUnavailable;
            resposta.ContentType = "application/json; charset=utf-8";
            resposta.Headers.CacheControl = "no-store, no-cache";
            resposta.Headers.Pragma = "no-cache";
            resposta.Headers.Expires = "Thu, 01 Jan 1970 00:00:00 GMT";

            if (!HttpMethods.IsHead(context.Request.Method))
                await resposta.WriteAsJsonAsync(new EstadoDaSaude(relatorio.Status.ToString()), context.RequestAborted);
        }
    }

    internal sealed record EstadoDaSaude(string Status);

    internal sealed class Sonda
    {
        public static Sonda Marcador { get; } = new();
    }

    internal sealed class SondaSqlServer(SqlServerHealthCheck sql, ILogger<SondaSqlServer> logger) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
        {
            using var prazo = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            prazo.CancelAfter(TimeSpan.FromSeconds(PrazoEmSegundos));
            var resultado = await sql.CheckHealthAsync(context, prazo.Token);
            cancellationToken.ThrowIfCancellationRequested();

            if (resultado.Status == HealthStatus.Healthy && !prazo.IsCancellationRequested)
                return HealthCheckResult.Healthy();

            // O serviço nativo também registra a exceção quando ela vem no resultado da sonda.
            var codigoSql = (resultado.Exception as SqlException)?.Number;
            logger.LogWarning("Readiness do SQL Server indisponível. Tipo: {TipoErro}. SQL: {CodigoSql}. Tempo limite: {TempoLimite}",
                resultado.Exception?.GetType().Name, codigoSql, prazo.IsCancellationRequested || codigoSql == -2);
            return HealthCheckResult.Unhealthy();
        }
    }
}
