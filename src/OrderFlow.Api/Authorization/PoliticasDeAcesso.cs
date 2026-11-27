using OrderFlow.Domain.Enums;
using Microsoft.AspNetCore.Authorization;

namespace OrderFlow.Api.Authorization;

internal static class PoliticasDeAcesso
{
    public const string Operacao = "Operacao";
    public const string Provisionamento = "Provisionamento";

    public static void Configurar(AuthorizationOptions options)
    {
        var operacao = ExigirPerfil(nameof(PerfilUsuario.Admin), nameof(PerfilUsuario.Operator));
        options.AddPolicy(Operacao, operacao);
        options.AddPolicy(Provisionamento, ExigirPerfil(nameof(PerfilUsuario.Admin)));
        options.DefaultPolicy = operacao;
        options.FallbackPolicy = operacao;
    }

    private static AuthorizationPolicy ExigirPerfil(params string[] perfis) => new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireRole(perfis)
        .Build();
}
