using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Ecommerce.Infrastructure.Configuration;

public sealed class JwtConfiguration
{
    public const string RoleClaimName = "role";

    private const int MinimumKeySizeInBytes = 32;
    private const int MinimumExpirationMinutes = 1;
    private const int MaximumExpirationMinutes = 60;

    private JwtConfiguration(
        string issuer,
        string audience,
        int expirationMinutes,
        SigningCredentials signingCredentials)
    {
        Issuer = issuer;
        Audience = audience;
        ExpirationMinutes = expirationMinutes;
        SigningCredentials = signingCredentials;
    }

    public string Issuer { get; }
    public string Audience { get; }
    public int ExpirationMinutes { get; }
    public SigningCredentials SigningCredentials { get; }

    public static JwtConfiguration Load(IConfiguration configuration)
    {
        var section = configuration.GetSection("Jwt");
        var issuer = GetRequiredValue(section, "Issuer");
        var audience = GetRequiredValue(section, "Audience");
        var key = section["Chave"];

        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < MinimumKeySizeInBytes)
        {
            throw new InvalidOperationException(
                $"Defina Jwt__Chave com uma chave de pelo menos {MinimumKeySizeInBytes} bytes em UTF-8 para HS256.");
        }

        // A seção permite distinguir uma chave ausente de uma chave presente com valor nulo.
        var expiration = section.GetChildren().FirstOrDefault(setting =>
            string.Equals(setting.Key, "ExpiraMinutos", StringComparison.OrdinalIgnoreCase));
        var expirationMinutes = MaximumExpirationMinutes;
        if (expiration is not null &&
            (!int.TryParse(expiration.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out expirationMinutes) ||
             expirationMinutes is < MinimumExpirationMinutes or > MaximumExpirationMinutes))
        {
            throw new InvalidOperationException(
                $"Defina Jwt__ExpiraMinutos como um número inteiro entre {MinimumExpirationMinutes} e {MaximumExpirationMinutes}.");
        }

        var signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        return new JwtConfiguration(issuer, audience, expirationMinutes, signingCredentials);
    }

    private static string GetRequiredValue(IConfigurationSection section, string key)
    {
        var value = section[key];
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"Defina Jwt__{key} com um valor não vazio.");
    }

    public TokenValidationParameters CreateTokenValidationParameters() => new()
    {
        RequireSignedTokens = true,
        RequireExpirationTime = true,
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateIssuerSigningKey = true,
        ValidateLifetime = true,
        IgnoreTrailingSlashWhenValidatingAudience = false,
        AudienceValidator = (audiences, _, parameters) => audiences is not null &&
            audiences.SequenceEqual([parameters.ValidAudience], StringComparer.Ordinal),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ValidIssuer = Issuer,
        ValidAudience = Audience,
        IssuerSigningKey = SigningCredentials.Key,
        NameClaimType = JwtRegisteredClaimNames.Sub,
        RoleClaimType = RoleClaimName,
        ClockSkew = TimeSpan.Zero
    };
}
