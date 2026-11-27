using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OrderFlow.Application.Dtos.Requests;
using OrderFlow.Application.Dtos.Responses;
using OrderFlow.Domain.Entities;
using OrderFlow.Domain.Enums;
using OrderFlow.Infrastructure.Configuration;
using OrderFlow.Infrastructure.Data;
using OrderFlow.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace OrderFlow.Tests;

public sealed class AutorizacaoApiTests(AutorizacaoApiFixture fixture) : IClassFixture<AutorizacaoApiFixture>
{
    private const string Cadastro = "/api/v1/auth/registrar";
    private const string Fallback = "/_test/fallback";
    private const long InicioDe2000 = 946684800L;
    private const long InicioDe2099 = 4070908800L;
    private static readonly string[] RotasOperacionais = ["/api/v1/compradores", "/api/v1/produtos", "/api/v1/pedidos"];

    [SqlServerFact]
    public async Task FallbackEAuthorizeSemPolicy_ExigemPerfilOperacional()
    {
        foreach (var rota in new[] { Fallback, "/_test/identidade" })
        {
            using var anonimo = await EnviarAsync(HttpMethod.Get, rota, token: null);
            await VerificarDesafioGenericoAsync(anonimo);

            foreach (var (token, status) in new[]
            {
                (fixture.CriarTokenDeTeste([]), HttpStatusCode.Forbidden),
                (fixture.CriarTokenDeTeste(["Desconhecido"]), HttpStatusCode.Forbidden),
                (fixture.CriarTokenDeTeste(["Admin", "Operator"]), HttpStatusCode.Forbidden),
                (fixture.CriarTokenDeTeste(["Admin"], new Claim("roles", "Admin")), HttpStatusCode.Forbidden),
                (fixture.AdminToken, HttpStatusCode.OK),
                (fixture.OperatorToken, HttpStatusCode.OK)
            })
            {
                using var resposta = await EnviarAsync(HttpMethod.Get, rota, token);
                Assert.Equal(status, resposta.StatusCode);
            }
        }

        var endpoint = Assert.Single(fixture.Api.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.OfType<RouteEndpoint>(), e => $"/{e.RoutePattern.RawText?.TrimStart('/')}" == Fallback);
        Assert.Empty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
    }

    [SqlServerFact]
    public async Task RotasInexistentesEMetodosNaoMapeados_PassamPelaFallback()
    {
        const string inexistente = "/api/v1/rota-inexistente";
        foreach (var (metodo, rota, esperado) in new[]
        {
            (HttpMethod.Get, inexistente, HttpStatusCode.NotFound),
            (HttpMethod.Delete, RotasOperacionais[0], HttpStatusCode.MethodNotAllowed),
            (HttpMethod.Post, "/health/live", HttpStatusCode.MethodNotAllowed)
        })
        {
            using var anonimo = await EnviarAsync(metodo, rota, token: null);
            await VerificarDesafioGenericoAsync(anonimo);
            using var semPerfil = await EnviarAsync(metodo, rota, fixture.CriarTokenDeTeste([]));
            Assert.Equal(HttpStatusCode.Forbidden, semPerfil.StatusCode);
            using var operador = await EnviarAsync(metodo, rota, fixture.OperatorToken);
            Assert.Equal(esperado, operador.StatusCode);
        }
    }

    [SqlServerFact]
    public async Task Fallback_CompartilhaCotaOperacionalPorUsuario()
    {
        using var api = new AutorizacaoApiFixture.ApiFactory(new Dictionary<string, string?>(fixture.Database.Configuration)
        {
            ["RateLimiting:OperacoesPorUsuario"] = "1"
        });
        using var client = api.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var metodoNaoMapeado = await EnviarAsync(HttpMethod.Delete, RotasOperacionais[0], fixture.OperatorToken, client: client);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, metodoNaoMapeado.StatusCode);
        using var permitida = await EnviarAsync(HttpMethod.Get, Fallback, fixture.OperatorToken, client: client);
        Assert.Equal(HttpStatusCode.OK, permitida.StatusCode);
        using var limitada = await EnviarAsync(HttpMethod.Get, RotasOperacionais[0], fixture.OperatorToken, client: client);
        Assert.Equal(HttpStatusCode.TooManyRequests, limitada.StatusCode);
        using var outroUsuario = await EnviarAsync(HttpMethod.Get, RotasOperacionais[0], fixture.AdminToken, client: client);
        Assert.Equal(HttpStatusCode.OK, outroUsuario.StatusCode);
    }

    [SqlServerFact]
    public void Inventario_ExigeRevisaoDeRotasMetodosEExcecoesAnonimas()
    {
        string[] esperado =
        [
            "POST /api/v1/auth/login Anônimo",
            "POST /api/v1/auth/registrar Provisionamento",
            "GET /api/v1/compradores Operacao", "POST /api/v1/compradores Operacao",
            "GET /api/v1/compradores/{id:guid} Operacao",
            "GET /api/v1/produtos Operacao", "POST /api/v1/produtos Operacao",
            "GET /api/v1/produtos/{id:guid} Operacao",
            "GET /api/v1/pedidos Operacao", "POST /api/v1/pedidos Operacao",
            "GET /api/v1/pedidos/{id:guid} Operacao", "PUT /api/v1/pedidos/{id:guid} Operacao",
            "DELETE /api/v1/pedidos/{id:guid} Operacao",
            "PATCH /api/v1/pedidos/{id:guid}/processar Operacao",
            "PATCH /api/v1/pedidos/{id:guid}/enviar Operacao",
            "PATCH /api/v1/pedidos/{id:guid}/cancelar Operacao",
            "GET /health Anônimo", "HEAD /health Anônimo",
            "GET /health/live Anônimo", "HEAD /health/live Anônimo",
            "GET /health/ready Anônimo", "HEAD /health/ready Anônimo"
        ];

        var rotas = fixture.Api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType() !=
                typeof(IdentidadeDeTesteController) &&
                endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType() !=
                typeof(FallbackDeTesteController));
        var inventario = rotas.SelectMany(endpoint =>
        {
            var autorizacoes = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
            Assert.Empty(endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>());
            Assert.Empty(endpoint.Metadata.GetOrderedMetadata<IAuthorizationRequirementData>());
            var permissao = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null
                ? "Anônimo"
                : string.Join(",", autorizacoes.Select(autorizacao =>
                {
                    Assert.True(string.IsNullOrEmpty(autorizacao.Roles));
                    Assert.True(string.IsNullOrEmpty(autorizacao.AuthenticationSchemes));
                    return autorizacao.Policy;
                }));
            var metodos = Assert.IsAssignableFrom<IHttpMethodMetadata>(endpoint.Metadata.GetMetadata<IHttpMethodMetadata>());
            Assert.NotEmpty(metodos.HttpMethods);
            return metodos.HttpMethods.Select(metodo => $"{metodo} /{endpoint.RoutePattern.RawText!.TrimStart('/')} {permissao}");
        });
        Assert.Equal(esperado.Order(StringComparer.Ordinal), inventario.Order(StringComparer.Ordinal));
    }

    [SqlServerFact]
    public async Task MutacoesNaoAutorizadas_NaoConsultamNemAlteramOBanco()
    {
        var comprador = new Comprador { Nome = "Comprador de teste", Email = $"{Guid.NewGuid():N}@example.test" };
        var produto = new Produto { Nome = "Produto de teste", Preco = 10m };
        var iniciado = PedidoComStatus(StatusPedido.Iniciado);
        var processado = PedidoComStatus(StatusPedido.Processado);
        await using (var inicial = fixture.Database.CreateContext())
        {
            inicial.AddRange(iniciado, processado);
            await inicial.SaveChangesAsync();
        }

        var banco = new ComandosObservaveis();
        using var api = fixture.Api.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddScoped<ApplicationDbContext>(_ => fixture.Database.CreateContext(banco))));
        using var client = api.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var compradorNovo = $"{Guid.NewGuid():N}@example.test";
        var produtoNovo = Guid.NewGuid().ToString();
        var itens = new[] { new { produtoId = produto.Id, quantidade = 2 } };
        (HttpMethod Metodo, string Rota, object? Corpo)[] mutacoes =
        [
            (HttpMethod.Post, RotasOperacionais[0], new { nome = "Comprador novo", email = compradorNovo }),
            (HttpMethod.Post, RotasOperacionais[1], new { nome = produtoNovo, preco = 20m }),
            (HttpMethod.Post, RotasOperacionais[2], new { compradorId = comprador.Id, itens }),
            (HttpMethod.Put, $"/api/v1/pedidos/{iniciado.Id}", new { itens }),
            (HttpMethod.Patch, $"/api/v1/pedidos/{iniciado.Id}/processar", null),
            (HttpMethod.Patch, $"/api/v1/pedidos/{processado.Id}/enviar", null),
            (HttpMethod.Patch, $"/api/v1/pedidos/{iniciado.Id}/cancelar", null),
            (HttpMethod.Delete, $"/api/v1/pedidos/{iniciado.Id}", null)
        ];
        var semPerfil = fixture.CriarTokenDeTeste([]);
        foreach (var (metodo, rota, corpo) in mutacoes)
        {
            using var anonimo = await EnviarAsync(metodo, rota, null, corpo, client);
            Assert.Equal(HttpStatusCode.Unauthorized, anonimo.StatusCode);
            using var proibido = await EnviarAsync(metodo, rota, semPerfil, corpo, client);
            Assert.Equal(HttpStatusCode.Forbidden, proibido.StatusCode);
        }

        var conta = NovaConta();
        using var cadastroAnonimo = await EnviarAsync(HttpMethod.Post, Cadastro, null, conta, client);
        Assert.Equal(HttpStatusCode.Unauthorized, cadastroAnonimo.StatusCode);
        using var cadastroOperador = await EnviarAsync(HttpMethod.Post, Cadastro, fixture.OperatorToken, conta, client);
        Assert.Equal(HttpStatusCode.Forbidden, cadastroOperador.StatusCode);
        Assert.Empty(banco.Executados);

        await using var final = fixture.Database.CreateContext();
        Assert.False(await final.Usuarios.AnyAsync(u => u.Email == conta.Email));
        Assert.False(await final.Compradores.AnyAsync(c => c.Email == compradorNovo));
        Assert.False(await final.Produtos.AnyAsync(p => p.Nome == produtoNovo));
        var pedidos = await final.Pedidos.Include(p => p.Itens).Where(p => p.CompradorId == comprador.Id).ToListAsync();
        Assert.Equal(2, pedidos.Count);
        Assert.Equal(StatusPedido.Iniciado, Assert.Single(pedidos, p => p.Id == iniciado.Id).Status);
        Assert.Equal(StatusPedido.Processado, Assert.Single(pedidos, p => p.Id == processado.Id).Status);
        Assert.All(pedidos, pedido => Assert.Equal(1, Assert.Single(pedido.Itens).Quantidade));

        Pedido PedidoComStatus(StatusPedido status) => new()
        {
            Comprador = comprador,
            Status = status,
            Itens = [new ItemPedido { Produto = produto, Quantidade = 1, PrecoUnitario = produto.Preco }]
        };
    }

    [SqlServerFact]
    public async Task Swagger_EmDevelopmentProtegeHtmlContraEnquadramento()
    {
        using var response = await fixture.Client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        TransporteHttpTests.VerificarCabecalhos(response, html: true);
        Assert.False(response.Headers.Contains("Content-Security-Policy"));

        using var bundle = await fixture.Client.GetAsync("/swagger/swagger-ui-bundle.js");
        Assert.Equal(HttpStatusCode.OK, bundle.StatusCode);
        TransporteHttpTests.VerificarCabecalhos(bundle);
    }

    [SqlServerFact]
    public async Task Anonimo_NaoPodeCadastrarContas()
    {
        using var response = await EnviarAsync(HttpMethod.Post, Cadastro, token: null, NovaConta());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        TransporteHttpTests.VerificarCabecalhos(response);
    }

    [SqlServerFact]
    public async Task OperadorAutenticado_NaoPodeCadastrarContas()
    {
        using var response = await EnviarAsync(HttpMethod.Post, Cadastro, fixture.OperatorToken, NovaConta());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        TransporteHttpTests.VerificarCabecalhos(response);
    }

    [SqlServerFact]
    public async Task Admin_PodeProvisionarOperadorComRolePersistidaEEmitidaNoLogin()
    {
        var novo = NovaConta();
        using var response = await EnviarAsync(HttpMethod.Post, Cadastro, fixture.AdminToken, novo);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        TransporteHttpTests.VerificarCabecalhos(response);
        await using var context = fixture.Database.CreateContext();
        var usuario = await context.Usuarios.SingleAsync(usuario => usuario.Email == novo.Email);
        Assert.Equal(PerfilUsuario.Operator, usuario.Perfil);
        var jwt = await fixture.LoginAsync(novo);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(jwt);
        Assert.Equal("Operator", Assert.Single(token.Claims, claim => claim.Type == "role").Value);
        Assert.Equal(usuario.Id.ToString(), token.Subject);
        Assert.Equal(SecurityAlgorithms.HmacSha256, token.Header.Alg);
        Assert.Equal(new[] { "aud", "exp", "iss", "role", "sub" }, token.Payload.Keys.Order(StringComparer.Ordinal));

        using var identidade = await EnviarAsync(HttpMethod.Get, "/_test/identidade", jwt);
        Assert.Equal(HttpStatusCode.OK, identidade.StatusCode);
        using var corpo = JsonDocument.Parse(await identidade.Content.ReadAsStringAsync());
        Assert.Equal(usuario.Id.ToString(), corpo.RootElement.GetProperty("nomeDaIdentidade").GetString());
        Assert.Equal(usuario.Id.ToString(), corpo.RootElement.GetProperty("sub").GetString());
        Assert.Equal("Operator", corpo.RootElement.GetProperty("role").GetString());
        Assert.True(corpo.RootElement.GetProperty("operador").GetBoolean());
        Assert.False(corpo.RootElement.GetProperty("claimMapeada").GetBoolean());
    }

    [SqlServerFact]
    public async Task Cadastro_RecusaDuplicidadeSequencialComConflito()
    {
        var conta = NovaConta();
        using var primeiro = await EnviarAsync(HttpMethod.Post, Cadastro, fixture.AdminToken, conta);
        conta.Email = $"  {conta.Email.ToUpperInvariant()}  ";
        using var duplicado = await EnviarAsync(HttpMethod.Post, Cadastro, fixture.AdminToken, conta);

        Assert.Equal(HttpStatusCode.NoContent, primeiro.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicado.StatusCode);
        Assert.True(duplicado.Headers.CacheControl?.NoStore);
        TransporteHttpTests.VerificarCabecalhos(duplicado);
        using var corpo = JsonDocument.Parse(await duplicado.Content.ReadAsStringAsync());
        Assert.Equal("Já existe um usuário cadastrado com este e-mail.",
            Assert.Single(corpo.RootElement.EnumerateObject()).Value.GetString());
    }

    [SqlServerFact]
    public async Task Cadastro_RejeitaCamposDeIdentidadeEPerfilMesmoQuandoSolicitadoPorAdmin()
    {
        await using var context = fixture.Database.CreateContext();
        foreach (var (campo, valor) in new[]
        {
            ("role", "Admin"), ("perfil", "Admin"), ("id", Guid.NewGuid().ToString()), ("sub", Guid.NewGuid().ToString())
        })
        {
            var novo = NovaConta();
            using var response = await EnviarAsync(HttpMethod.Post, Cadastro, fixture.AdminToken,
                new Dictionary<string, string>
                {
                    ["nome"] = novo.Nome,
                    ["email"] = novo.Email,
                    ["senha"] = novo.Senha,
                    [campo] = valor
                });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            TransporteHttpTests.VerificarCabecalhos(response);
            Assert.False(await context.Usuarios.AnyAsync(usuario => usuario.Email == novo.Email));
        }
    }

    [SqlServerFact]
    public async Task JwtLegado_ComNomeEEmailContinuaAutorizadoComIdentidadeBaseadaEmSub()
    {
        var jwt = fixture.Database.CreateJwtConfiguration();
        var payload = CriarPayloadValido(jwt);
        payload["role"] = "Operator";
        payload[ClaimTypes.Name] = "Funcionário legado";
        payload[JwtRegisteredClaimNames.Email] = "legado@example.test";
        var token = SerializarToken(payload, jwt.SigningCredentials);
        var sub = Assert.IsType<string>(payload["sub"]);

        using var operacao = await EnviarAsync(HttpMethod.Get, RotasOperacionais[0], token);
        Assert.Equal(HttpStatusCode.OK, operacao.StatusCode);
        using var identidade = await EnviarAsync(HttpMethod.Get, "/_test/identidade", token);
        Assert.Equal(HttpStatusCode.OK, identidade.StatusCode);
        using var corpo = JsonDocument.Parse(await identidade.Content.ReadAsStringAsync());
        Assert.Equal(sub, corpo.RootElement.GetProperty("nomeDaIdentidade").GetString());
    }

    [SqlServerFact]
    public async Task Cadastro_AplicaPoliticaELoginPreservaFraseSenhaUnicodeComEspacos()
    {
        await using var context = fixture.Database.CreateContext();
        var conta = NovaConta();

        foreach (var senha in new[] { conta.Senha[..14], PoliticaDeSenhasTests.Frase(129), PoliticaDeSenhasTests.SenhaComum })
        {
            conta.Senha = senha;
            using var response = await EnviarAsync(HttpMethod.Post, Cadastro, fixture.AdminToken, conta);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(await context.Usuarios.AnyAsync(u => u.Email == conta.Email));
        }

        conta.Senha = PoliticaDeSenhasTests.Frase(128);
        using var cadastro = await EnviarAsync(HttpMethod.Post, Cadastro, fixture.AdminToken, conta);
        Assert.Equal(HttpStatusCode.NoContent, cadastro.StatusCode);
        Assert.NotEmpty(await fixture.LoginAsync(conta));

        using var semEspacos = await EnviarAsync(HttpMethod.Post, "/api/v1/auth/login", token: null,
            new LoginRequest { Email = conta.Email, Senha = conta.Senha.Trim() });
        Assert.Equal(HttpStatusCode.Unauthorized, semEspacos.StatusCode);
    }

    [SqlServerFact]
    public async Task CadastroELogin_RejeitamUnicodeMalformadoSemEcoarSenha()
    {
        const string senha = "Frase de teste privada";
        var conta = NovaConta();
        foreach (var caractere in new[] { @"\uD800", @"\uDC00" })
        {
            var dadosCadastro = $$"""{"nome":"{{conta.Nome}}","email":"{{conta.Email}}","senha":"{{senha}}{{caractere}}"}""";
            using var cadastro = new HttpRequestMessage(HttpMethod.Post, Cadastro)
            {
                Content = new StringContent(dadosCadastro, Encoding.UTF8, "application/json")
            };
            cadastro.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.AdminToken);
            using var respostaCadastro = await fixture.Client.SendAsync(cadastro);

            var dadosLogin = $$"""{"email":"{{conta.Email}}","senha":"{{senha}}{{caractere}}"}""";
            using var credenciais = new StringContent(dadosLogin, Encoding.UTF8, "application/json");
            using var respostaLogin = await fixture.Client.PostAsync("/api/v1/auth/login", credenciais);
            foreach (var resposta in new[] { respostaCadastro, respostaLogin })
            {
                Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
                Assert.True(resposta.Headers.CacheControl?.NoStore);
                Assert.DoesNotContain(senha, await resposta.Content.ReadAsStringAsync());
            }
        }

        await using var context = fixture.Database.CreateContext();
        Assert.False(await context.Usuarios.AnyAsync(u => u.Email == conta.Email));
    }

    [SqlServerFact]
    public async Task Login_RetornaMesmoDesafioParaCredenciaisInvalidasEMantemValidacao400()
    {
        var conta = NovaConta();
        var contaCorrompida = NovaConta();
        var contaLegada = NovaConta();
        await using var context = fixture.Database.CreateContext();
        context.Usuarios.AddRange(
            new Usuario { Id = Guid.NewGuid(), Nome = conta.Nome, Email = conta.Email, SenhaHash = HashDeSenha.Gerar(conta.Senha) },
            new Usuario { Id = Guid.NewGuid(), Nome = contaCorrompida.Nome, Email = contaCorrompida.Email, SenhaHash = "invalido" },
            new Usuario { Id = Guid.NewGuid(), Nome = contaLegada.Nome, Email = contaLegada.Email,
                SenhaHash = HashDeSenhaTests.GerarHashDeTeste(contaLegada.Senha, 100_000) });
        await context.SaveChangesAsync();
        Assert.NotEmpty(await fixture.LoginAsync(conta));

        var falhas = new[]
        {
            new LoginRequest { Email = conta.Email, Senha = conta.Senha + "x" },
            new LoginRequest { Email = contaLegada.Email, Senha = contaLegada.Senha + "x" },
            new LoginRequest { Email = NovaConta().Email, Senha = HashDeSenhaTests.EntradaDoVetorDeReferencia },
            new LoginRequest { Email = contaCorrompida.Email, Senha = HashDeSenhaTests.EntradaDoVetorDeReferencia }
        };
        foreach (var credenciais in falhas)
        {
            using var resposta = await EnviarAsync(HttpMethod.Post, "/api/v1/auth/login", token: null, credenciais);
            Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
            var desafio = Assert.Single(resposta.Headers.WwwAuthenticate);
            Assert.Equal("OrderFlowLogin", desafio.Scheme);
            Assert.Null(desafio.Parameter);
            Assert.True(resposta.Headers.CacheControl?.NoStore);
            using var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
            var propriedade = Assert.Single(corpo.RootElement.EnumerateObject());
            Assert.Equal("mensagem", propriedade.Name);
            Assert.Equal("E-mail ou senha inválidos.", propriedade.Value.GetString());
        }

        using var extensa = await EnviarAsync(HttpMethod.Post, "/api/v1/auth/login", token: null,
            new LoginRequest { Email = conta.Email, Senha = PoliticaDeSenhasTests.Frase(1025) });
        Assert.Equal(HttpStatusCode.BadRequest, extensa.StatusCode);
        Assert.True(extensa.Headers.CacheControl?.NoStore);
        Assert.Empty(extensa.Headers.WwwAuthenticate);
        Assert.Contains("1024", await extensa.Content.ReadAsStringAsync());
    }

    [SqlServerFact]
    public async Task AdminEOperator_PodemAcessarCadaControllerOperacional()
    {
        foreach (var token in new[] { fixture.AdminToken, fixture.OperatorToken })
        {
            foreach (var rota in RotasOperacionais)
            {
                using var response = await EnviarAsync(HttpMethod.Get, rota, token);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            using var criacao = await EnviarAsync(HttpMethod.Post, "/api/v1/produtos", token,
                new { nome = "Produto de teste", preco = 10 });
            Assert.Equal(HttpStatusCode.Created, criacao.StatusCode);
        }
    }

    [SqlServerFact]
    public async Task MudancaDePerfil_AfetaNovoLoginEPreservaIdentificador()
    {
        var conta = NovaConta();
        using var cadastro = await EnviarAsync(HttpMethod.Post, Cadastro, fixture.AdminToken, conta);
        Assert.Equal(HttpStatusCode.NoContent, cadastro.StatusCode);
        var tokenAnterior = await fixture.LoginAsync(conta);

        await using var context = fixture.Database.CreateContext();
        var usuario = await context.Usuarios.SingleAsync(u => u.Email == conta.Email);
        usuario.Perfil = null;
        await context.SaveChangesAsync();

        var tokenAtual = await fixture.LoginAsync(conta);
        var leitor = new JwtSecurityTokenHandler();
        Assert.Equal(leitor.ReadJwtToken(tokenAnterior).Subject, leitor.ReadJwtToken(tokenAtual).Subject);
        using var anterior = await EnviarAsync(HttpMethod.Get, RotasOperacionais[0], tokenAnterior);
        using var atual = await EnviarAsync(HttpMethod.Get, RotasOperacionais[0], tokenAtual);
        Assert.Equal(HttpStatusCode.OK, anterior.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, atual.StatusCode);
    }

    [SqlServerFact]
    public async Task TokensValidosSemPerfilComPerfilDesconhecidoOuComVariosPerfis_Recebem403()
    {
        var jwt = fixture.Database.CreateJwtConfiguration();
        var payload = CriarPayloadValido(jwt);
        payload["role"] = new[] { "Admin" };
        string[][] perfisInvalidos =
        {
            [], ["Desconhecido"], ["admin"], ["Admin", "Desconhecido"], ["Admin", "Operator"], ["Admin", "Admin"]
        };
        var tokens = perfisInvalidos.Select(perfis => fixture.CriarTokenDeTeste(perfis)).Concat(
        [
            fixture.CriarTokenDeTeste([], new Claim("roles", "Admin")),
            fixture.CriarTokenDeTeste([], new Claim(ClaimTypes.Role, "Admin")),
            fixture.CriarTokenDeTeste([], new Claim("ROLE", "Admin")),
            fixture.CriarTokenDeTeste(["Admin"], new Claim("ROLE", "Operator")),
            fixture.CriarTokenDeTeste(["Admin"], new Claim("roles", "Operator")),
            fixture.CriarTokenDeTeste(["Admin"], new Claim(ClaimTypes.Role, "Operator")),
            SerializarToken(payload, jwt.SigningCredentials)
        ]);
        foreach (var token in tokens)
        {
            foreach (var rota in RotasOperacionais)
            {
                using var response = await EnviarAsync(HttpMethod.Get, rota, token);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }

            using var cadastro = await EnviarAsync(HttpMethod.Post, Cadastro, token, NovaConta());
            Assert.Equal(HttpStatusCode.Forbidden, cadastro.StatusCode);
        }
    }

    [SqlServerTheory]
    [InlineData(SecurityAlgorithms.HmacSha256, false, HttpStatusCode.OK)]
    [InlineData(SecurityAlgorithms.HmacSha256, true, HttpStatusCode.Unauthorized)]
    [InlineData(SecurityAlgorithms.HmacSha384, false, HttpStatusCode.Unauthorized)]
    [InlineData(SecurityAlgorithms.HmacSha512, false, HttpStatusCode.Unauthorized)]
    [InlineData(SecurityAlgorithms.None, false, HttpStatusCode.Unauthorized)]
    public async Task Jwt_ExigeAssinaturaHs256ComAChaveConfigurada(string algoritmo, bool outraChave, HttpStatusCode status)
    {
        var jwt = fixture.Database.CreateJwtConfiguration();
        var key = outraChave ? new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)) : jwt.SigningCredentials.Key;
        var credenciais = algoritmo == SecurityAlgorithms.None ? null : new SigningCredentials(key, algoritmo);
        var token = SerializarToken(CriarPayloadValido(jwt), credenciais);

        using var response = await EnviarAsync(HttpMethod.Get, RotasOperacionais[0], token);

        Assert.Equal(status, response.StatusCode);
        if (status == HttpStatusCode.Unauthorized)
            await VerificarDesafioGenericoAsync(response);
    }

    [SqlServerTheory]
    [InlineData("exp", null)]
    [InlineData("exp", InicioDe2000)]
    [InlineData("exp", "4102444800")]
    [InlineData("nbf", InicioDe2099)]
    [InlineData("nbf", "946684800")]
    [InlineData("iss", null)]
    [InlineData("iss", "emissor-de-outro-servico")]
    [InlineData("iss", "orderflow.tests")]
    [InlineData("iss", "OrderFlow.Tests/")]
    [InlineData("aud", null)]
    [InlineData("aud", "outro-servico")]
    [InlineData("aud", "orderflow.tests")]
    [InlineData("aud", "OrderFlow.Tests/")]
    [InlineData("aud", new string[] { "OrderFlow.Tests", "outro-servico" })]
    [InlineData("aud", new string[] { "outro-servico", "OrderFlow.Tests" })]
    [InlineData("aud", new string[] { "OrderFlow.Tests", "OrderFlow.Tests" })]
    [InlineData("sub", null)]
    [InlineData("sub", "")]
    [InlineData("sub", "identificador-invalido")]
    [InlineData("sub", "00000000-0000-0000-0000-000000000000")]
    [InlineData("sub", " 22345678-1234-1234-1234-123456789012 ")]
    [InlineData("sub", new string[] { "22345678-1234-1234-1234-123456789012" })]
    [InlineData("sub", new string[] { "22345678-1234-1234-1234-123456789012", "32345678-1234-1234-1234-123456789012" })]
    public async Task Jwt_ComClaimObrigatoriaAusenteOuInvalidaRecebe401SemDetalhes(string claim, object? valor)
    {
        var jwt = fixture.Database.CreateJwtConfiguration();
        var payload = CriarPayloadValido(jwt);
        if (valor is null)
            payload.Remove(claim);
        else
            payload[claim] = valor;

        using var response = await EnviarAsync(HttpMethod.Get, RotasOperacionais[0],
            SerializarToken(payload, jwt.SigningCredentials));

        await VerificarDesafioGenericoAsync(response);
    }

    [SqlServerFact]
    public async Task Jwt_AceitaUmaAudienceExataEmArrayENbfNoPassado()
    {
        var jwt = fixture.Database.CreateJwtConfiguration();
        var payload = CriarPayloadValido(jwt);
        payload["aud"] = new[] { jwt.Audience };
        payload["nbf"] = InicioDe2000;

        using var response = await EnviarAsync(HttpMethod.Get, RotasOperacionais[0],
            SerializarToken(payload, jwt.SigningCredentials));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [SqlServerTheory]
    [InlineData("sub", null, HttpStatusCode.Unauthorized)]
    [InlineData("sub", "22345678-1234-1234-1234-123456789012", HttpStatusCode.Unauthorized)]
    [InlineData("iss", null, HttpStatusCode.Unauthorized)]
    [InlineData("aud", "outro-servico", HttpStatusCode.Unauthorized)]
    [InlineData("role", null, HttpStatusCode.Forbidden)]
    [InlineData("role", "Operator", HttpStatusCode.Forbidden)]
    public async Task Jwt_RejeitaPropriedadesJsonRepetidasMesmoQuandoOsValoresCoincidem(
        string campo, string? valor, HttpStatusCode status)
    {
        var jwt = fixture.Database.CreateJwtConfiguration();
        var payload = CriarPayloadValido(jwt);
        var repeticao = JsonSerializer.Serialize(valor ?? payload[campo]);
        var token = AssinarPayloadJson(payload.SerializeToJson().Insert(1, $"\"{campo}\":{repeticao},"), jwt.SigningCredentials);

        using var operacao = await EnviarAsync(HttpMethod.Get, RotasOperacionais[0], token);
        using var cadastro = await EnviarAsync(HttpMethod.Post, Cadastro, token, NovaConta());

        Assert.Equal(status, operacao.StatusCode);
        Assert.Equal(status, cadastro.StatusCode);
        if (status == HttpStatusCode.Unauthorized)
            await VerificarDesafioGenericoAsync(operacao);
    }

    [SqlServerTheory]
    [InlineData(ClaimTypes.NameIdentifier, true)]
    [InlineData(ClaimTypes.NameIdentifier, false)]
    [InlineData("nameid", false)]
    [InlineData("SUB", false)]
    public async Task Jwt_AliasesDeIdentidadeRecebem401(string alias, bool omitirSub)
    {
        var jwt = fixture.Database.CreateJwtConfiguration();
        var payload = CriarPayloadValido(jwt);
        if (omitirSub)
            payload.Remove("sub");
        payload[alias] = Guid.NewGuid().ToString("D");

        using var response = await EnviarAsync(HttpMethod.Get, RotasOperacionais[0],
            SerializarToken(payload, jwt.SigningCredentials));

        await VerificarDesafioGenericoAsync(response);
    }

    private static JwtPayload CriarPayloadValido(JwtConfiguration jwt) => new(
        jwt.Issuer, jwt.Audience,
        [new Claim("sub", Guid.NewGuid().ToString("D")), new Claim("role", "Admin")],
        notBefore: null, expires: new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    private static string SerializarToken(JwtPayload payload, SigningCredentials? credenciais) =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(new JwtHeader(credenciais), payload));

    private static string AssinarPayloadJson(string payload, SigningCredentials credenciais)
    {
        var partesAssinadas = $"{new JwtHeader(credenciais).Base64UrlEncode()}.{Base64UrlEncoder.Encode(payload)}";
        var chave = Assert.IsType<SymmetricSecurityKey>(credenciais.Key);
        var assinatura = HMACSHA256.HashData(chave.Key, Encoding.UTF8.GetBytes(partesAssinadas));
        return $"{partesAssinadas}.{Base64UrlEncoder.Encode(assinatura)}";
    }

    private static async Task VerificarDesafioGenericoAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var desafio = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Equal("Bearer", desafio.Scheme);
        Assert.Null(desafio.Parameter);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [SqlServerFact]
    public async Task TokensAusentesOuInvalidos_Recebem401()
    {
        foreach (var token in new string?[] { null, "token-invalido" })
        {
            foreach (var rota in RotasOperacionais)
            {
                using var response = await EnviarAsync(HttpMethod.Get, rota, token);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }
        }
    }

    [SqlServerFact]
    public async Task Swagger_DocumentaBearerEPerfisSomenteNasRotasProtegidas()
    {
        using var respostaSwagger = await fixture.Client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, respostaSwagger.StatusCode);
        Assert.Equal("application/json", respostaSwagger.Content.Headers.ContentType?.MediaType);
        TransporteHttpTests.VerificarCabecalhos(respostaSwagger);
        using var swagger = JsonDocument.Parse(await respostaSwagger.Content.ReadAsStringAsync());
        var paths = swagger.RootElement.GetProperty("paths");
        var login = paths.GetProperty("/api/v1/auth/login").GetProperty("post");
        Assert.False(login.TryGetProperty("security", out _));
        var respostasLogin = login.GetProperty("responses");
        Assert.True(respostasLogin.TryGetProperty("200", out _));
        Assert.True(respostasLogin.TryGetProperty("400", out _));
        var credenciaisInvalidas = respostasLogin.GetProperty("401");
        Assert.Equal("E-mail ou senha inválidos.", credenciaisInvalidas.GetProperty("description").GetString());
        Assert.Equal("OrderFlowLogin", EsquemaDoDesafio(credenciaisInvalidas));
        Assert.Equal("application/json", Assert.Single(credenciaisInvalidas.GetProperty("content").EnumerateObject()).Name);

        var cadastro = paths.GetProperty(Cadastro).GetProperty("post");
        Assert.Equal("Perfil Admin obrigatório.", cadastro.GetProperty("description").GetString());
        Assert.True(cadastro.GetProperty("security")[0].TryGetProperty("Bearer", out _));
        var respostasCadastro = cadastro.GetProperty("responses");
        Assert.Equal("Bearer", EsquemaDoDesafio(respostasCadastro.GetProperty("401")));
        Assert.True(respostasCadastro.TryGetProperty("400", out _));
        Assert.True(respostasCadastro.TryGetProperty("403", out _));
        var conflito = respostasCadastro.GetProperty("409");
        Assert.Equal("application/json", Assert.Single(conflito.GetProperty("content").EnumerateObject()).Name);

        foreach (var rota in RotasOperacionais)
        {
            var criacao = paths.GetProperty(rota).GetProperty("post").GetProperty("responses");
            Assert.True(criacao.TryGetProperty("201", out _));
            Assert.False(criacao.TryGetProperty("200", out _));
            var entradaInvalida = criacao.GetProperty("400").GetProperty("content");
            Assert.True(entradaInvalida.TryGetProperty("application/json", out _));
            Assert.True(entradaInvalida.TryGetProperty("application/problem+json", out _));
            var busca = paths.GetProperty($"{rota}/{{id}}").GetProperty("get").GetProperty("responses");
            Assert.True(busca.GetProperty("404").GetProperty("content").TryGetProperty("application/json", out _));
        }

        var pedido = paths.GetProperty("/api/v1/pedidos/{id}");
        Assert.True(pedido.GetProperty("put").GetProperty("responses").TryGetProperty("204", out _));
        Assert.True(pedido.GetProperty("delete").GetProperty("responses").TryGetProperty("204", out _));

        var operacoes = paths.EnumerateObject()
            .Where(path => path.Name == Fallback || RotasOperacionais.Any(rota => path.Name.StartsWith(rota, StringComparison.Ordinal)))
            .SelectMany(path => path.Value.EnumerateObject().Select(operacao => operacao.Value)).ToArray();
        Assert.Equal(15, operacoes.Length);
        foreach (var operacao in operacoes)
        {
            Assert.Contains("Perfil Admin ou Operator obrigatório.", operacao.GetProperty("description").GetString());
            Assert.True(operacao.GetProperty("security")[0].TryGetProperty("Bearer", out _));
            var respostas = operacao.GetProperty("responses");
            Assert.Equal("Bearer", EsquemaDoDesafio(respostas.GetProperty("401")));
            Assert.True(respostas.TryGetProperty("403", out _));
        }

        foreach (var operacao in operacoes.Append(login).Append(cadastro))
        {
            var excesso = operacao.GetProperty("responses").GetProperty("429");
            Assert.True(excesso.GetProperty("content").TryGetProperty("application/json", out _));
            var cabecalhos = excesso.GetProperty("headers");
            Assert.True(cabecalhos.TryGetProperty("Retry-After", out _));
            Assert.True(cabecalhos.TryGetProperty("Cache-Control", out _));
            Assert.True(operacao.GetProperty("responses").GetProperty("500").GetProperty("content")
                .TryGetProperty("application/json", out _));
        }

        foreach (var rota in new[] { "/health/live", "/health/ready", "/health" })
        {
            var path = paths.GetProperty(rota);
            foreach (var metodo in new[] { "get", "head" })
            {
                var operacao = path.GetProperty(metodo);
                Assert.False(operacao.TryGetProperty("security", out _));
                var respostas = operacao.GetProperty("responses");
                Assert.False(respostas.TryGetProperty("401", out _));
                Assert.False(respostas.TryGetProperty("403", out _));
                var corpoInvalido = respostas.GetProperty("400");
                Assert.False(corpoInvalido.TryGetProperty("content", out _));
                Assert.Equal("no-store", corpoInvalido.GetProperty("headers").GetProperty("Cache-Control")
                    .GetProperty("schema").GetProperty("enum")[0].GetString());
                var readiness = rota != "/health/live";
                Assert.Equal(readiness, respostas.TryGetProperty("503", out _));
                foreach (var codigo in readiness ? new[] { "200", "503", "429" } : ["200", "429"])
                {
                    var resposta = respostas.GetProperty(codigo);
                    Assert.True(resposta.GetProperty("headers").TryGetProperty("Cache-Control", out _));
                    Assert.Equal(metodo == "get", resposta.TryGetProperty("content", out var conteudo) &&
                        conteudo.TryGetProperty("application/json", out _));
                }
            }
        }

        static string? EsquemaDoDesafio(JsonElement resposta) => resposta
            .GetProperty("headers").GetProperty("WWW-Authenticate").GetProperty("schema").GetProperty("enum")[0].GetString();
    }

    internal static RegistrarUsuarioRequest NovaConta() => new()
    {
        Nome = "Conta de teste",
        Email = $"{Guid.NewGuid():N}@example.test",
        Senha = Convert.ToHexString(RandomNumberGenerator.GetBytes(16))
    };

    private async Task<HttpResponseMessage> EnviarAsync(HttpMethod method, string rota, string? token, object? body = null,
        HttpClient? client = null)
    {
        using var request = new HttpRequestMessage(method, rota);
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await (client ?? fixture.Client).SendAsync(request);
    }
}

public sealed class AutorizacaoApiFixture : IAsyncLifetime
{
    private WebApplicationFactory<Program>? _factory;
    internal SqlServerTestDatabase Database { get; private set; } = null!;
    internal HttpClient Client { get; private set; } = null!;
    internal string AdminToken { get; private set; } = null!;
    internal string OperatorToken { get; private set; } = null!;

    internal WebApplicationFactory<Program> Api => _factory!;

    public async Task InitializeAsync()
    {
        if (SqlServerTestDatabase.PodeIgnorarTestes)
            return;

        Database = new SqlServerTestDatabase();
        var admin = AutorizacaoApiTests.NovaConta();
        var operador = AutorizacaoApiTests.NovaConta();

        await using (var context = Database.CreateContext())
        {
            await context.Database.MigrateAsync();
            var auth = Database.CreateAuthService(context);
            await auth.ProvisionarPrimeiroAdminAsync(admin);
            await auth.RegistrarAsync(operador);
        }

        // As regressões compartilham host e sujeitos. Os testes de limites criam hosts com cotas próprias.
        _factory = new ApiFactory(new Dictionary<string, string?>(Database.Configuration)
        {
            ["RateLimiting:CadastroPorAdmin"] = "100",
            ["RateLimiting:LoginPorOrigem"] = "100"
        });
        Client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        AdminToken = await LoginAsync(admin);
        OperatorToken = await LoginAsync(operador);
    }

    internal async Task<string> LoginAsync(RegistrarUsuarioRequest conta)
    {
        using var response = await Client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Email = conta.Email,
            Senha = conta.Senha
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        return Assert.IsType<LoginResponse>(login).Token;
    }

    internal string CriarTokenDeTeste(IEnumerable<string> perfis, params Claim[] outrasClaims)
    {
        var jwt = Database.CreateJwtConfiguration();
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()) };
        claims.AddRange(perfis.Select(perfil => new Claim("role", perfil)));
        claims.AddRange(outrasClaims);

        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            jwt.Issuer, jwt.Audience, claims, expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: jwt.SigningCredentials));
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        if (Database is not null)
            await Database.DisposeAsync();
    }

    internal sealed class ApiFactory(IReadOnlyDictionary<string, string?> configuration) : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(configuration));
            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.ConfigureServices(services =>
            services.AddControllers().AddApplicationPart(typeof(IdentidadeDeTesteController).Assembly));
    }
}

[Authorize]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("/_test/identidade")]
public sealed class IdentidadeDeTesteController : ControllerBase
{
    [HttpGet]
    public IActionResult Obter() => Ok(new
    {
        nomeDaIdentidade = User.Identity?.Name,
        sub = User.FindFirstValue("sub"),
        role = User.FindFirstValue("role"),
        operador = User.IsInRole("Operator"),
        claimMapeada = User.HasClaim(claim => claim.Type is ClaimTypes.NameIdentifier or ClaimTypes.Role)
    });
}

[ApiController]
[Route("/_test/fallback")]
public sealed class FallbackDeTesteController : ControllerBase
{
    [HttpGet]
    public IActionResult Obter() => Ok();

    [HttpGet("falha/{entrada}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public IActionResult Falhar(string entrada)
    {
        HttpContext.TraceIdentifier = "rastreio-de-teste";
        Response.Headers["X-Dados-Internos"] = entrada;
        Response.ContentLength = 1;
        throw new InvalidOperationException(entrada, new FormatException($"Falha interna: {entrada}"));
    }
}
