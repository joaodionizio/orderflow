namespace Ecommerce.Application.Security;

public static class EmailDeUsuario
{
    public static string Normalizar(string email) => email.Trim().ToLowerInvariant();
}
