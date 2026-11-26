using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Ecommerce.Application.Dtos.Requests;
using Ecommerce.Application.Dtos.Responses;
using Ecommerce.Application.Exceptions;
using Ecommerce.Application.Interfaces;
using Ecommerce.Application.Security;
using Ecommerce.Domain.Entities;
using Ecommerce.Domain.Enums;
using Ecommerce.Infrastructure.Configuration;
using Ecommerce.Infrastructure.Data;
using Ecommerce.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ecommerce.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly ApplicationDbContext _context;
    private readonly JwtConfiguration _jwt;
    private readonly ILogger<AuthService> _logger;

    public AuthService(ApplicationDbContext context, JwtConfiguration jwt, ILogger<AuthService>? logger = null)
    {
        _context = context;
        _jwt = jwt;
        _logger = logger ?? NullLogger<AuthService>.Instance;
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

        var erroSenha = PoliticaDeSenhas.ValidarNova(request.Senha);
        if (erroSenha is not null)
            throw new RegraDeNegocioException(erroSenha);

        return new Usuario
        {
            Id = Guid.NewGuid(),
            Nome = request.Nome.Trim(),
            Email = request.Email.Trim().ToLowerInvariant(),
            SenhaHash = HashDeSenha.Gerar(request.Senha),
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
        if (PoliticaDeSenhas.ValidarLogin(request.Senha) is not null)
            throw CredenciaisInvalidas();

        var email = request.Email.Trim().ToLowerInvariant();
        var usuario = await _context.Usuarios
            .AsNoTracking()
            .FirstOrDefaultAsync(usuario => usuario.Email == email) ?? throw CredenciaisInvalidas();

        if (VerificarCredencial(request.Senha, usuario) == ResultadoHashDeSenha.AtualizacaoNecessaria)
        {
            var id = usuario.Id;
            var hashAnterior = usuario.SenhaHash;
            var hashNovo = HashDeSenha.Gerar(request.Senha);

            // O SQL Server deve comparar também maiúsculas, minúsculas e o comprimento real do registro.
            var afetados = await _context.Usuarios
                .Where(u => u.Id == id
                    && EF.Functions.Collate(u.SenhaHash, "Latin1_General_100_BIN2") == hashAnterior
                    && EF.Functions.DataLength(u.SenhaHash) == EF.Functions.DataLength(hashAnterior))
                .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.SenhaHash, hashNovo));

            if (afetados == 0)
            {
                usuario = await _context.Usuarios.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == id && u.Email == email) ?? throw CredenciaisInvalidas();
                VerificarCredencial(request.Senha, usuario);
            }
        }

        return GerarToken(usuario);
    }

    private ResultadoHashDeSenha VerificarCredencial(string senha, Usuario usuario)
    {
        var resultado = HashDeSenha.Verificar(senha, usuario.SenhaHash);
        if (resultado == ResultadoHashDeSenha.RegistroInvalido)
            _logger.LogWarning("Registro de hash de senha inválido encontrado durante a autenticação.");

        if (resultado is ResultadoHashDeSenha.RegistroInvalido or ResultadoHashDeSenha.SenhaIncorreta)
            throw CredenciaisInvalidas();

        return resultado;
    }

    private static RegraDeNegocioException CredenciaisInvalidas() => new("E-mail ou senha inválidos.");

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
}
