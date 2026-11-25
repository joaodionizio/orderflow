using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Ecommerce.Application.Dtos.Requests;
using Ecommerce.Application.Dtos.Responses;
using Ecommerce.Application.Exceptions;
using Ecommerce.Application.Interfaces;
using Ecommerce.Domain.Entities;
using Ecommerce.Domain.Enums;
using Ecommerce.Infrastructure.Configuration;
using Ecommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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
        var usuario = CriarUsuario(request, PerfilUsuario.Operator);
        await GarantirEmailDisponivelAsync(usuario.Email);
        _context.Usuarios.Add(usuario);
        await _context.SaveChangesAsync();
    }

    public async Task ProvisionarPrimeiroAdminAsync(RegistrarUsuarioRequest request)
    {
        if (request.Senha is not { Length: >= 15 })
            throw new RegraDeNegocioException("A senha do administrador deve possuir pelo menos 15 caracteres.");

        var usuario = CriarUsuario(request, PerfilUsuario.Admin);
        var strategy = _context.Database.CreateExecutionStrategy();

        try
        {
            await strategy.ExecuteInTransactionAsync(
                async cancellationToken =>
                {
                    if (await _context.Usuarios.AnyAsync(u => u.Perfil == PerfilUsuario.Admin, cancellationToken))
                        throw new RegraDeNegocioException("Já existe um administrador. O provisionamento inicial foi encerrado.");

                    await GarantirEmailDisponivelAsync(usuario.Email, cancellationToken);
                    _context.Usuarios.Add(usuario);
                    await _context.SaveChangesAsync(acceptAllChangesOnSuccess: false, cancellationToken);
                },
                cancellationToken => _context.Usuarios.AsNoTracking().AnyAsync(
                    u => u.Id == usuario.Id && u.Perfil == PerfilUsuario.Admin, cancellationToken),
                IsolationLevel.Serializable,
                CancellationToken.None);
        }
        catch
        {
            _context.Entry(usuario).State = EntityState.Detached;
            throw;
        }

        _context.ChangeTracker.AcceptAllChanges();
    }

    private static Usuario CriarUsuario(RegistrarUsuarioRequest request, PerfilUsuario perfil)
    {
        if (string.IsNullOrWhiteSpace(request.Nome))
            throw new RegraDeNegocioException("O nome do usuário é obrigatório.");

        if (string.IsNullOrWhiteSpace(request.Email))
            throw new RegraDeNegocioException("O e-mail do usuário é obrigatório.");

        if (string.IsNullOrWhiteSpace(request.Senha))
            throw new RegraDeNegocioException("A senha é obrigatória.");

        return new Usuario
        {
            Id = Guid.NewGuid(),
            Nome = request.Nome.Trim(),
            Email = request.Email.Trim().ToLowerInvariant(),
            SenhaHash = GerarHash(request.Senha),
            Perfil = perfil,
            DataCriacao = DateTime.UtcNow
        };
    }

    private async Task GarantirEmailDisponivelAsync(string email, CancellationToken cancellationToken = default)
    {
        if (await _context.Usuarios.AnyAsync(usuario => usuario.Email == email, cancellationToken))
            throw new RegraDeNegocioException("Já existe um usuário cadastrado com este e-mail.");
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

        if (usuario.Perfil is { } perfil)
            claims.Add(new Claim("role", perfil.ToString()));

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
