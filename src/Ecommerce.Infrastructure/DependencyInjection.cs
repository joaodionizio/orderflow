using Ecommerce.Application.Interfaces;
using Ecommerce.Infrastructure.Data;
using Ecommerce.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ecommerce.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(
                connectionString,
                sqlOptions => sqlOptions.EnableRetryOnFailure()));

        services.AddScoped<ICompradorService, CompradorService>();
        services.AddScoped<IProdutoService, ProdutoService>();
        services.AddScoped<IPedidoService, PedidoService>();
        services.AddScoped<AuthService>();
        services.AddScoped<IAuthService>(provider => provider.GetRequiredService<AuthService>());

        return services;
    }
}
