using Ecommerce.Application.Interfaces;
using Ecommerce.Infrastructure.Data;
using Ecommerce.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Ecommerce.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(
                connectionString,
                sqlOptions => sqlOptions.EnableRetryOnFailure())
            // Exceções do servidor podem conter valores mesmo sem logging de dados sensíveis.
            .ConfigureWarnings(warnings => warnings.Ignore(
                RelationalEventId.CommandError, RelationalEventId.ConnectionError, RelationalEventId.TransactionError,
                CoreEventId.SaveChangesFailed, CoreEventId.QueryIterationFailed, CoreEventId.ExecutionStrategyRetrying)));

        services.AddScoped<ICompradorService, CompradorService>();
        services.AddScoped<IProdutoService, ProdutoService>();
        services.AddScoped<IPedidoService, PedidoService>();
        services.AddScoped<AuthService>();
        services.AddScoped<IAuthService>(provider => provider.GetRequiredService<AuthService>());

        return services;
    }
}
