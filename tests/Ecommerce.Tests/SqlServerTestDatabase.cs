using System.Security.Cryptography;
using Ecommerce.Infrastructure.Configuration;
using Ecommerce.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;

namespace Ecommerce.Tests;

internal sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (SqlServerTestDatabase.PodeIgnorarTestes)
            Skip = "Defina ORDERFLOW_TEST_SQLSERVER com uma instância SQL Server isolada.";
    }
}

internal sealed class SqlServerTestDatabase : IAsyncDisposable
{
    private static string? ConfiguredConnection => Environment.GetEnvironmentVariable("ORDERFLOW_TEST_SQLSERVER");

    public static bool PodeIgnorarTestes => string.IsNullOrWhiteSpace(ConfiguredConnection)
        && !string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase);

    public SqlServerTestDatabase()
    {
        var connectionString = ConfiguredConnection;
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Defina ORDERFLOW_TEST_SQLSERVER com uma instância SQL Server isolada.");

        ConnectionString = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = $"OrderFlowTests_{Guid.NewGuid():N}"
        }.ConnectionString;

        Configuration = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = ConnectionString,
            ["Jwt:Issuer"] = "OrderFlow.Tests",
            ["Jwt:Audience"] = "OrderFlow.Tests",
            ["Jwt:Chave"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
        };
    }

    private string ConnectionString { get; }
    public IReadOnlyDictionary<string, string?> Configuration { get; }

    public ApplicationDbContext CreateContext(params IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlServer(ConnectionString, options => options.EnableRetryOnFailure())
        .AddInterceptors(interceptors)
        .Options);

    public JwtConfiguration CreateJwtConfiguration() => JwtConfiguration.Load(
        new ConfigurationBuilder().AddInMemoryCollection(Configuration).Build());

    public async ValueTask DisposeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }
}
