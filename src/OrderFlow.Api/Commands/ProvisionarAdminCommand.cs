using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using System.Text;
using OrderFlow.Application.Dtos.Requests;
using OrderFlow.Application.Exceptions;
using OrderFlow.Infrastructure.Data;
using OrderFlow.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace OrderFlow.Api.Commands;

internal static class ProvisionarAdminCommand
{
    public const string Nome = "provision-admin";

    public static Task<int> ExecutarAsync(IServiceProvider services)
    {
        if (Console.IsInputRedirected)
            return Task.FromResult(Erro(Console.Error, "Execute o provisionamento em um terminal interativo."));

        if (!OperatingSystem.IsWindows())
            Console.InputEncoding = Encoding.GetEncoding(Console.InputEncoding.CodePage,
                EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

        return ExecutarAsync(services, Console.In, Console.Out, Console.Error,
            () => Console.ReadKey(intercept: true));
    }

    internal static async Task<int> ExecutarAsync(IServiceProvider services, TextReader entrada,
        TextWriter saida, TextWriter erros, Func<ConsoleKeyInfo> lerTecla)
    {
        await using var scope = services.CreateAsyncScope();
        var contexto = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        try
        {
            if ((await contexto.Database.GetPendingMigrationsAsync()).Any())
                return Erro(erros, "Aplique as migrations do OrderFlow antes de provisionar o administrador.");

            var conexao = contexto.Database.GetDbConnection();
            saida.WriteLine($"Provisionamento inicial em {conexao.DataSource}, banco {conexao.Database}.");
            saida.Write("Nome do administrador: ");
            var nome = entrada.ReadLine() ?? string.Empty;
            saida.Write("E-mail do administrador: ");
            var email = entrada.ReadLine() ?? string.Empty;
            var senha = LerSenha("Senha: ", saida, lerTecla);

            if (!string.Equals(senha, LerSenha("Confirme a senha: ", saida, lerTecla), StringComparison.Ordinal))
                return Erro(erros, "As senhas informadas não coincidem.");

            var request = new RegistrarUsuarioRequest { Nome = nome, Email = email, Senha = senha };
            var validacoes = new List<ValidationResult>();
            if (!Validator.TryValidateObject(request, new ValidationContext(request), validacoes, validateAllProperties: true))
                return Erro(erros, string.Join(Environment.NewLine, validacoes.Select(erro => erro.ErrorMessage)));

            var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
            await auth.ProvisionarPrimeiroAdminAsync(request);
            saida.WriteLine("Administrador inicial provisionado.");
            return 0;
        }
        catch (RegraDeNegocioException ex)
        {
            return Erro(erros, ex.Message);
        }
        catch (DecoderFallbackException)
        {
            return Erro(erros, "A entrada contém caracteres Unicode inválidos.");
        }
        catch (Exception ex) when (ex is DbException or DbUpdateException or RetryLimitExceededException)
        {
            return Erro(erros, "Não foi possível provisionar o administrador. Verifique o estado do banco e suas permissões.");
        }
    }

    private static string LerSenha(string prompt, TextWriter saida, Func<ConsoleKeyInfo> lerTecla)
    {
        saida.Write(prompt);
        var senha = new StringBuilder();

        while (true)
        {
            var tecla = lerTecla();
            if (tecla.Key == ConsoleKey.Enter)
            {
                saida.WriteLine();
                return senha.ToString();
            }

            if (tecla.Key == ConsoleKey.Backspace)
            {
                if (senha.Length > 0)
                {
                    var temPar = senha.Length >= 2 && char.IsSurrogatePair(senha[senha.Length - 2], senha[senha.Length - 1]);
                    senha.Length -= temPar ? 2 : 1;
                }
            }
            // ReadKey também usa '\0' para teclas sem caractere, como as setas.
            else if (tecla.KeyChar != '\0' || (tecla.Key is ConsoleKey.D2 or ConsoleKey.Spacebar
                && (tecla.Modifiers & ConsoleModifiers.Control) != 0))
                senha.Append(tecla.KeyChar);
        }
    }

    private static int Erro(TextWriter erros, string mensagem)
    {
        erros.WriteLine(mensagem);
        return 1;
    }
}
