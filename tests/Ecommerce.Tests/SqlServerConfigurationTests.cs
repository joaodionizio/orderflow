using Ecommerce.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;

namespace Ecommerce.Tests;

public class SqlServerConfigurationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetRequiredConnectionString_RejeitaValorAusenteOuEmBranco(string? valor)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => SqlServerConfiguration.GetRequiredConnectionString(CriarConfiguracao(valor)));

        Assert.Contains("ConnectionStrings__DefaultConnection", exception.Message);
        Assert.Contains("obrigatória", exception.Message);
    }

    [Fact]
    public void GetRequiredConnectionString_RejeitaSintaxeInvalidaSemRevelarOValor()
    {
        const string marcador = "marcador-artificial";
        var configuracao = CriarConfiguracao($"Server=localhost;{marcador}=valor");

        var exception = Assert.Throws<InvalidOperationException>(
            () => SqlServerConfiguration.GetRequiredConnectionString(configuracao));

        Assert.Contains("ConnectionStrings__DefaultConnection", exception.Message);
        Assert.Contains("inválida", exception.Message);
        Assert.DoesNotContain(marcador, exception.ToString());
    }

    [Theory]
    [InlineData("Database=OrderFlow")]
    [InlineData("Server='   ';Database=OrderFlow")]
    public void GetRequiredConnectionString_RejeitaServidorAusenteOuEmBranco(string valor)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => SqlServerConfiguration.GetRequiredConnectionString(CriarConfiguracao(valor)));

        Assert.Contains("ConnectionStrings__DefaultConnection", exception.Message);
        Assert.Contains("Data Source", exception.Message);
        Assert.DoesNotContain(valor, exception.Message);
    }

    [Theory]
    [InlineData("Server=localhost,1433;Database=OrderFlow;Integrated Security=True")]
    [InlineData("Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=OrderFlow;Integrated Security=True")]
    public void GetRequiredConnectionString_PreservaValorValido(string valor)
    {
        var connectionString = SqlServerConfiguration.GetRequiredConnectionString(CriarConfiguracao(valor));

        Assert.Equal(valor, connectionString);
    }

    private static IConfiguration CriarConfiguracao(string? valor)
    {
        var configuracao = new ConfigurationManager();
        if (valor is not null)
            configuracao["ConnectionStrings:DefaultConnection"] = valor;

        return configuracao;
    }
}
