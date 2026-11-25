using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Ecommerce.Infrastructure.Configuration;

public static class SqlServerConfiguration
{
    public static string GetRequiredConnectionString(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "A string de conexão do SQL Server é obrigatória. Defina a variável de ambiente ConnectionStrings__DefaultConnection.");
        }

        SqlConnectionStringBuilder connectionOptions;
        try
        {
            connectionOptions = new SqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException(
                "A string de conexão do SQL Server é inválida. Verifique o formato de ConnectionStrings__DefaultConnection.");
        }

        if (string.IsNullOrWhiteSpace(connectionOptions.DataSource))
        {
            throw new InvalidOperationException(
                "Defina um servidor SQL Server (Data Source ou Server) em ConnectionStrings__DefaultConnection.");
        }

        return connectionString;
    }
}
