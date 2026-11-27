using System.Security.Claims;
using System.Text.Json;
using Ecommerce.Domain.Enums;
using Ecommerce.Infrastructure.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using JwtRegisteredClaimNames = System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames;

namespace Ecommerce.Api.Authorization;

internal static class ValidacaoDeClaimsJwt
{
    public static Task ValidarAsync(TokenValidatedContext context)
    {
        if (context.SecurityToken is not JsonWebToken token ||
            context.Principal?.Identity is not ClaimsIdentity identidade ||
            token.ValidTo <= DateTime.UtcNow ||
            !TentarLerClaims(token, out var perfil))
        {
            context.Fail("Claims JWT inválidas.");
            return Task.CompletedTask;
        }

        if (perfil is null)
        {
            foreach (var claim in identidade.FindAll(CampoDePerfil).ToArray())
                identidade.RemoveClaim(claim);
        }

        return Task.CompletedTask;
    }

    private static bool TentarLerClaims(JsonWebToken token, out string? perfil)
    {
        perfil = null;
        // A leitura do JSON assinado preserva propriedades repetidas e distingue valores escalares de arrays.
        try
        {
            using var payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(token.EncodedPayload));
            return payload.RootElement.ValueKind == JsonValueKind.Object &&
                ValidarPayload(payload.RootElement, out perfil);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool ValidarPayload(JsonElement payload, out string? perfil)
    {
        perfil = null;
        var camposUnicos = new HashSet<string>(StringComparer.Ordinal);
        var camposDePerfil = 0;

        foreach (var campo in payload.EnumerateObject())
        {
            if (CampoDePerfil(campo.Name))
            {
                camposDePerfil++;
                if (campo.NameEquals(JwtConfiguration.RoleClaimName) && campo.Value.ValueKind == JsonValueKind.String)
                    perfil = campo.Value.GetString();

                continue;
            }

            if (campo.Name.Equals("nameid", StringComparison.OrdinalIgnoreCase) ||
                campo.Name.Equals(ClaimTypes.NameIdentifier, StringComparison.OrdinalIgnoreCase) ||
                (campo.Name.Equals(JwtRegisteredClaimNames.Sub, StringComparison.OrdinalIgnoreCase) &&
                 !campo.NameEquals(JwtRegisteredClaimNames.Sub)))
            {
                return false;
            }

            if (campo.Name is JwtRegisteredClaimNames.Sub or JwtRegisteredClaimNames.Iss or
                JwtRegisteredClaimNames.Aud or JwtRegisteredClaimNames.Exp or JwtRegisteredClaimNames.Nbf)
            {
                if (!camposUnicos.Add(campo.Name) || !FormatoValido(campo))
                    return false;
            }
        }

        if (camposDePerfil != 1 || perfil is not (nameof(PerfilUsuario.Admin) or nameof(PerfilUsuario.Operator)))
            perfil = null;

        return camposUnicos.Contains(JwtRegisteredClaimNames.Sub);
    }

    private static bool FormatoValido(JsonProperty campo) => campo.Name switch
    {
        JwtRegisteredClaimNames.Sub => campo.Value.ValueKind == JsonValueKind.String &&
            campo.Value.GetString() is { Length: 36 } sub &&
            Guid.TryParseExact(sub, "D", out var id) && id != Guid.Empty,
        JwtRegisteredClaimNames.Iss => campo.Value.ValueKind == JsonValueKind.String,
        JwtRegisteredClaimNames.Aud => campo.Value.ValueKind == JsonValueKind.String ||
            (campo.Value.ValueKind == JsonValueKind.Array && campo.Value.GetArrayLength() == 1 &&
             campo.Value[0].ValueKind == JsonValueKind.String),
        JwtRegisteredClaimNames.Exp or JwtRegisteredClaimNames.Nbf =>
            campo.Value.ValueKind == JsonValueKind.Number && campo.Value.TryGetInt64(out _),
        _ => false
    };

    private static bool CampoDePerfil(Claim claim) => CampoDePerfil(claim.Type);

    private static bool CampoDePerfil(string nome) =>
        nome.Equals(JwtConfiguration.RoleClaimName, StringComparison.OrdinalIgnoreCase) ||
        nome.Equals("roles", StringComparison.OrdinalIgnoreCase) ||
        nome.Equals(ClaimTypes.Role, StringComparison.OrdinalIgnoreCase);
}
