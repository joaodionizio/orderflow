using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using System.Text;
using Ecommerce.Application.Dtos.Requests;
using Ecommerce.Application.Exceptions;
using Ecommerce.Infrastructure.Data;
using Ecommerce.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Ecommerce.Api.Commands;

internal static class ProvisionarAdminCommand
{
    public const string Nome = "provision-admin";

    public static async Task<int> ExecutarAsync(IServiceProvider services)
    {
        if (Console.IsInputRedirected)
            return Erro("Execute o provisionamento em um terminal interativo.");

        await using var scope = services.CreateAsyncScope();
        var contexto = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        try
        {
            if ((await contexto.Database.GetPendingMigrationsAsync()).Any())
                return Erro("Aplique as migrations do OrderFlow antes de provisionar o administrador.");

            var conexao = contexto.Database.GetDbConnection();
            Console.WriteLine($"Provisionamento inicial em {conexao.DataSource}, banco {conexao.Database}.");
            Console.Write("Nome do administrador: ");
            var nome = Console.ReadLine() ?? string.Empty;
            Console.Write("E-mail do administrador: ");
            var email = Console.ReadLine() ?? string.Empty;
            var senha = LerSenha("Senha: ");

            if (!string.Equals(senha, LerSenha("Confirme a senha: "), StringComparison.Ordinal))
                return Erro("As senhas informadas não coincidem.");

            var request = new RegistrarUsuarioRequest { Nome = nome, Email = email, Senha = senha };
            var erros = new List<ValidationResult>();
            if (!Validator.TryValidateObject(request, new ValidationContext(request), erros, validateAllProperties: true))
                return Erro(string.Join(Environment.NewLine, erros.Select(erro => erro.ErrorMessage)));

            var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
            await auth.ProvisionarPrimeiroAdminAsync(request);
            Console.WriteLine("Administrador inicial provisionado.");
            return 0;
        }
        catch (RegraDeNegocioException ex)
        {
            return Erro(ex.Message);
        }
        catch (Exception ex) when (ex is DbException or DbUpdateException or RetryLimitExceededException)
        {
            return Erro("Não foi possível provisionar o administrador. Verifique o estado do banco e suas permissões.");
        }
    }

    private static string LerSenha(string prompt)
    {
        Console.Write(prompt);
        var senha = new StringBuilder();

        while (true)
        {
            var tecla = Console.ReadKey(intercept: true);
            if (tecla.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return senha.ToString();
            }

            if (tecla.Key == ConsoleKey.Backspace && senha.Length > 0)
                senha.Length--;
            else if (!char.IsControl(tecla.KeyChar))
                senha.Append(tecla.KeyChar);
        }
    }

    private static int Erro(string mensagem)
    {
        Console.Error.WriteLine(mensagem);
        return 1;
    }
}
