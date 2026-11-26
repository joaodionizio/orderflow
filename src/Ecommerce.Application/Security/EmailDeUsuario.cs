namespace Ecommerce.Application.Security;

public static class EmailDeUsuario
{
    public const int ComprimentoMaximo = 200;

    public static string Normalizar(string email) => email.Trim().ToLowerInvariant();
}
