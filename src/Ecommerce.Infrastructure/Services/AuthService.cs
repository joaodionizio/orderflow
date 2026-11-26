using System.Data;
using System.Diagnostics.CodeAnalysis;
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
        try
        {
            if (_context.Database.IsSqlServer())
            {
                var strategy = _context.Database.CreateExecutionStrategy();
                await strategy.ExecuteInTransactionAsync(
                    async cancellationToken =>
                    {
                        // Mantém o bloqueio da chave (ou do intervalo) até confirmar a inserção.
                        var emailEmUso = await _context.Database.SqlQuery<int>(
                            $"""
                            SELECT 1 AS [Value]
                            FROM [dbo].[Usuarios] WITH (UPDLOCK, HOLDLOCK, INDEX([IX_Usuarios_Email]))
                            WHERE [Email] = {usuario.Email}
                            """).AnyAsync(cancellationToken);
                        if (emailEmUso)
                            throw new EmailDeUsuarioEmUsoException();

                        _context.Usuarios.Add(usuario);
                        await _context.SaveChangesAsync(acceptAllChangesOnSuccess: false, cancellationToken);
                    },
                    cancellationToken => _context.Usuarios.AnyAsync(u => u.Id == usuario.Id && u.Email == usuario.Email,
                        cancellationToken),
                    CancellationToken.None);
                _context.ChangeTracker.AcceptAllChanges();
            }
            else
            {
                if (await EmailEmUsoAsync(usuario.Email))
                    throw new EmailDeUsuarioEmUsoException();

                _context.Usuarios.Add(usuario);
                await _context.SaveChangesAsync();
            }
        }
        catch
        {
            _context.Entry(usuario).State = EntityState.Detached;
            throw;
        }
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

                    if (await EmailEmUsoAsync(usuario.Email, cancellationToken))
                        throw new RegraDeNegocioException(EmailDeUsuarioEmUsoException.MensagemPublica);

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

        if (request.Email.Length > EmailDeUsuario.ComprimentoMaximo)
            throw new RegraDeNegocioException("Informe um e-mail dentro do comprimento permitido.");

        var erroSenha = PoliticaDeSenhas.ValidarNova(request.Senha);
        if (erroSenha is not null)
            throw new RegraDeNegocioException(erroSenha);

        return new Usuario
        {
            Id = Guid.NewGuid(),
            Nome = request.Nome.Trim(),
            Email = EmailDeUsuario.Normalizar(request.Email),
            SenhaHash = HashDeSenha.Gerar(request.Senha),
            Perfil = perfil,
            DataCriacao = DateTime.UtcNow
        };
    }

    private Task<bool> EmailEmUsoAsync(string email, CancellationToken cancellationToken = default) =>
        _context.Usuarios.AnyAsync(usuario => usuario.Email == email, cancellationToken);

    public async Task<LoginResponse> LoginAsync(LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > EmailDeUsuario.ComprimentoMaximo)
            throw new RegraDeNegocioException("Informe um e-mail dentro do comprimento permitido.");

        var erroSenha = PoliticaDeSenhas.ValidarLogin(request.Senha);
        if (erroSenha is not null)
            throw new RegraDeNegocioException(erroSenha);

        var email = EmailDeUsuario.Normalizar(request.Email);
        var usuario = await _context.Usuarios
            .AsNoTracking()
            .FirstOrDefaultAsync(usuario => usuario.Email == email);

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
                    .FirstOrDefaultAsync(u => u.Id == id && u.Email == email);
                VerificarCredencial(request.Senha, usuario);
            }
        }

        return GerarToken(usuario);
    }

    private ResultadoHashDeSenha VerificarCredencial(string senha, [NotNull] Usuario? usuario)
    {
        var resultado = HashDeSenha.VerificarParaLogin(senha, usuario?.SenhaHash, out _);
        if (usuario is not null && resultado == ResultadoHashDeSenha.RegistroInvalido)
            _logger.LogWarning("Registro de hash de senha inválido encontrado durante a autenticação.");

        if (usuario is null || resultado is ResultadoHashDeSenha.RegistroInvalido or ResultadoHashDeSenha.SenhaIncorreta)
            throw new CredenciaisInvalidasException();

        return resultado;
    }

    private LoginResponse GerarToken(Usuario usuario)
    {
        var expiraEm = DateTime.UtcNow.AddMinutes(_jwt.ExpirationMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, usuario.Id.ToString("D"))
        };

        if (usuario.Perfil is { } perfil)
            claims.Add(new Claim(JwtConfiguration.RoleClaimName, perfil.ToString()));

        var token = new JwtSecurityToken(
            _jwt.Issuer,
            _jwt.Audience,
            claims,
            expires: expiraEm,
            signingCredentials: _jwt.SigningCredentials);

        return new LoginResponse
        {
            Token = new JwtSecurityTokenHandler().WriteToken(token),
            ExpiraEm = token.ValidTo
        };
    }
}
