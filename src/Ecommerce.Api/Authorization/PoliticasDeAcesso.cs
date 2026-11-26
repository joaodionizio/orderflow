using Microsoft.AspNetCore.Authorization;

namespace Ecommerce.Api.Authorization;

internal static class PoliticasDeAcesso
{
    public const string Operacao = "Operacao";
    public const string Provisionamento = "Provisionamento";

    public static void ExigirPerfil(AuthorizationPolicyBuilder policy, params string[] perfis) => policy
        .RequireAuthenticatedUser()
        .RequireRole(perfis);
}
