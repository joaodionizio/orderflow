using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Ecommerce.Application.Dtos.Requests;
using Ecommerce.Application.Dtos.Responses;
using Ecommerce.Application.Exceptions;
using Ecommerce.Application.Interfaces;
using Ecommerce.Domain.Entities;
using Ecommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Ecommerce.Infrastructure.Services;

public class AuthService : IAuthService
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;

    public AuthService(ApplicationDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
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
        var chave = _configuration["Jwt:Chave"];

        if (string.IsNullOrWhiteSpace(chave))
            throw new InvalidOperationException("A chave JWT não foi configurada.");

        var issuer = _configuration["Jwt:Issuer"];
        var audience = _configuration["Jwt:Audience"];
        var expiraMinutos = int.TryParse(_configuration["Jwt:ExpiraMinutos"], out var minutos)
            ? minutos
            : 60;
        var expiraEm = DateTime.UtcNow.AddMinutes(expiraMinutos);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, usuario.Email),
            new(ClaimTypes.Name, usuario.Nome)
        };

        var credenciais = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(chave)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer,
            audience,
            claims,
            expires: expiraEm,
            signingCredentials: credenciais);

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
