using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Ecommerce.Infrastructure.Configuration;

public sealed class JwtConfiguration
{
    private const int MinimumKeySizeInBytes = 32;

    private JwtConfiguration(
        string? issuer,
        string? audience,
        int expirationMinutes,
        SigningCredentials signingCredentials)
    {
        Issuer = issuer;
        Audience = audience;
        ExpirationMinutes = expirationMinutes;
        SigningCredentials = signingCredentials;
    }

    public string? Issuer { get; }
    public string? Audience { get; }
    public int ExpirationMinutes { get; }
    public SigningCredentials SigningCredentials { get; }

    public static JwtConfiguration Load(IConfiguration configuration)
    {
        var section = configuration.GetSection("Jwt");
        var key = section["Chave"];

        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < MinimumKeySizeInBytes)
        {
            throw new InvalidOperationException(
                $"Defina Jwt__Chave com uma chave de pelo menos {MinimumKeySizeInBytes} bytes em UTF-8 para HS256.");
        }

        var expirationMinutes = int.TryParse(section["ExpiraMinutos"], out var minutes) ? minutes : 60;
        var signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        return new JwtConfiguration(section["Issuer"], section["Audience"], expirationMinutes, signingCredentials);
    }

    public TokenValidationParameters CreateTokenValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateIssuerSigningKey = true,
        ValidateLifetime = true,
        ValidIssuer = Issuer,
        ValidAudience = Audience,
        IssuerSigningKey = SigningCredentials.Key,
        ClockSkew = TimeSpan.Zero
    };
}
