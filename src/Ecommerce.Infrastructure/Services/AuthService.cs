using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Ecommerce.Application.Dtos.Requests;
using Ecommerce.Application.Dtos.Responses;
using Ecommerce.Application.Exceptions;
using Ecommerce.Application.Interfaces;
using Ecommerce.Domain.Entities;
using Ecommerce.Infrastructure.Configuration;
using Ecommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Services;

public class AuthService : IAuthService
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;
    private readonly ApplicationDbContext _context;
    private readonly JwtConfiguration _jwt;

    public AuthService(ApplicationDbContext context, JwtConfiguration jwt)
    {
        _context = context;
        _jwt = jwt;
    }

    public async Task RegistrarAsync(RegistrarUsuarioRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Nome))
            throw new RegraDeNegocioException("O nome do usuário é obrigatório.");

        if (string.IsNullOrWhiteSpace(request.Email))
            throw new RegraDeNegocioException("O e-mail do usuário é obrigatório.");

        if (string.IsNullOrWhiteSpace(request.Senha))
            throw new RegraDeNegocioException("A senha é obrigatória.");

        var email = request.Email.Trim().ToLowerInvariant();
        var emailJaCadastrado = await _context.Usuarios
            .AnyAsync(usuario => usuario.Email == email);

        if (emailJaCadastrado)
            throw new RegraDeNegocioException("Já existe um usuário cadastrado com este e-mail.");

        var usuario = new Usuario
        {
            Id = Guid.NewGuid(),
            Nome = request.Nome.Trim(),
            Email = email,
            SenhaHash = GerarHash(request.Senha),
            DataCriacao = DateTime.UtcNow
        };

        _context.Usuarios.Add(usuario);
        await _context.SaveChangesAsync();
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var usuario = await _context.Usuarios
            .AsNoTracking()
            .FirstOrDefaultAsync(usuario => usuario.Email == email);

        if (usuario is null || !SenhaValida(request.Senha, usuario.SenhaHash))
            throw new RegraDeNegocioException("E-mail ou senha inválidos.");

        return GerarToken(usuario);
    }

    private LoginResponse GerarToken(Usuario usuario)
    {
        var expiraEm = DateTime.UtcNow.AddMinutes(_jwt.ExpirationMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, usuario.Email),
            new(ClaimTypes.Name, usuario.Nome)
        };

        var token = new JwtSecurityToken(
            _jwt.Issuer,
            _jwt.Audience,
            claims,
            expires: expiraEm,
            signingCredentials: _jwt.SigningCredentials);

        return new LoginResponse
        {
            Token = new JwtSecurityTokenHandler().WriteToken(token),
            ExpiraEm = expiraEm
        };
    }

    private static string GerarHash(string senha)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            senha,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashSize);

        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    private static bool SenhaValida(string senha, string senhaHash)
    {
        var partes = senhaHash.Split('.', 3);

        if (partes.Length != 3 || !int.TryParse(partes[0], out var iteracoes))
            return false;

        var salt = Convert.FromBase64String(partes[1]);
        var hashSalvo = Convert.FromBase64String(partes[2]);
        var hashInformado = Rfc2898DeriveBytes.Pbkdf2(
            senha,
            salt,
            iteracoes,
            HashAlgorithmName.SHA256,
            hashSalvo.Length);

        return CryptographicOperations.FixedTimeEquals(hashInformado, hashSalvo);
    }
}
