using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ecommerce.Application.Dtos.Requests;
using Ecommerce.Application.Dtos.Responses;
using Ecommerce.Application.Exceptions;
using Ecommerce.Application.Interfaces;
using Ecommerce.Infrastructure.Data;
using Ecommerce.Infrastructure.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ecommerce.Tests;

public class ValidacaoDeEntradaApiTests(AutorizacaoApiFixture fixture) : IClassFixture<AutorizacaoApiFixture>
{
    private const string Login = "/api/v1/auth/login";
    private const string Cadastro = "/api/v1/auth/registrar";
    private const string Compradores = "/api/v1/compradores";
    private const string Produtos = "/api/v1/produtos";
    private const string Pedidos = "/api/v1/pedidos";

    [SqlServerFact]
    public async Task Auth_ValidaAntesDoServicoESuprimeDadosDoJsonNosLogsENasRespostas()
    {
        var auth = new AuthObservavel();
        var banco = new ComandosObservaveis();
        var logs = new CapturaDeLogs();
        using var api = CriarApi(banco, auth, logs);
        using var client = CriarCliente(api);
        banco.Executados.Clear();

        const string segredo = "SentinelaPrivada_98ABC670";
        var emailNoLimite = new string('a', 187) + "@example.test";
        var emailExcessivo = "b" + emailNoLimite;
        var loginValido = JsonSerializer.Serialize(new { email = emailNoLimite, senha = segredo });
        var conta = AutorizacaoApiTests.NovaConta();
        var cadastroInvalido = $$"""
            {"nome":"{{conta.Nome}}","email":"{{conta.Email}}","senha":"{{segredo}}","perfil":"Admin"}
            """;
        var entradas = new (string Rota, string Corpo, HttpStatusCode Status, string? Token, string Tipo)[]
        {
            (Login, loginValido.PadRight(16_385), HttpStatusCode.RequestEntityTooLarge, null, "application/json"),
            (Login, $$"""{"email":"{{emailExcessivo}}","senha":"{{segredo}}"}""", HttpStatusCode.BadRequest, null, "application/json"),
            (Login, $$"""{"senha":"{{segredo}}"}""", HttpStatusCode.BadRequest, null, "application/json"),
            (Login, """{"email":null,"senha":"   "}""", HttpStatusCode.BadRequest, null, "application/json"),
            (Login, """{"email":"teste@example.test","senha":null}""", HttpStatusCode.BadRequest, null, "application/json"),
            (Login, """{"email":"teste@example.test"}""", HttpStatusCode.BadRequest, null, "application/json"),
            (Login, $$"""{"{{segredo}}":1,"email":"teste@example.test","senha":"privada"}""", HttpStatusCode.BadRequest, null, "application/json"),
            (Login, $$"""{"email":"teste@example.test","senha":"{{segredo}}" """, HttpStatusCode.BadRequest, null, "application/json"),
            (Login, $$"""{"email":"teste@example.test","senha":"{{segredo}}\uD800"}""", HttpStatusCode.BadRequest, null, "application/json"),
            (Login, loginValido, HttpStatusCode.UnsupportedMediaType, null, "text/plain"),
            (Cadastro, cadastroInvalido, HttpStatusCode.BadRequest, fixture.AdminToken, "application/json"),
            (Cadastro, $$"""{"email":"{{conta.Email}}","senha":"{{segredo}}"}""", HttpStatusCode.BadRequest, fixture.AdminToken, "application/json"),
            (Cadastro, $$"""{"nome":"Conta","email":"{{emailExcessivo}}","senha":"{{segredo}}"}""", HttpStatusCode.BadRequest, fixture.AdminToken, "application/json")
        };

        foreach (var (rota, corpo, status, token, tipo) in entradas)
        {
            using var response = await EnviarAsync(client, HttpMethod.Post, rota, corpo, token, tipo);
            Assert.Equal(status, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.Empty(response.Headers.WwwAuthenticate);
            var resposta = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(segredo, resposta);
            Assert.DoesNotContain(emailExcessivo, resposta);
            Assert.DoesNotContain(token ?? fixture.AdminToken, resposta);
        }

        Assert.Empty(auth.Logins);
        Assert.Equal(0, auth.Cadastros);
        Assert.Empty(banco.Executados);
        Assert.NotEmpty(logs.Mensagens);
        Assert.DoesNotContain(logs.Mensagens, msg => msg.Contains(segredo) || msg.Contains(emailExcessivo)
            || msg.Contains(fixture.AdminToken));

        // O nome conhecido é aceito sem distinção de caixa e a última ocorrência prevalece.
        using var aceito = await EnviarAsync(client, HttpMethod.Post, Login,
            $$"""{"email":"anterior@example.test","EMAIL":"{{emailNoLimite}}","senha":"  Á🔐 {{segredo}}  "}""");
        Assert.Equal(HttpStatusCode.Unauthorized, aceito.StatusCode);
        var credenciais = Assert.Single(auth.Logins);
        Assert.Equal(emailNoLimite, credenciais.Email);
        Assert.Equal($"  Á🔐 {segredo}  ", credenciais.Senha);

        await using var context = fixture.Database.CreateContext();
        Assert.False(await context.Usuarios.AnyAsync(usuario => usuario.Email == conta.Email));
    }

    [SqlServerFact]
    public async Task Operacoes_RejeitamCamposInvalidosOuJsonProfundoSemExecutarSql()
    {
        var banco = new ComandosObservaveis();
        using var api = CriarApi(banco);
        using var client = CriarCliente(api);
        banco.Executados.Clear();

        var entradas = new (string Rota, string Corpo)[]
        {
            (Compradores, """{"nome":"   ","email":"comprador@example.test"}"""),
            (Compradores, """{"nome":"Comprador","email":null}"""),
            (Compradores, """{"nome":"Comprador","email":"sem-dominio"}"""),
            (Compradores, $$"""{"nome":"{{new string('a', 151)}}","email":"comprador@example.test"}"""),
            (Compradores, $$"""{"nome":"Comprador","email":"{{new string('a', 188)}}@example.test"}"""),
            (Produtos, """{"preco":1.00}"""),
            (Produtos, """{"nome":"Produto"}"""),
            (Produtos, """{"nome":"Produto","preco":null}"""),
            (Produtos, $$"""{"nome":"{{new string('a', 151)}}","preco":1.00}"""),
            (Produtos, """{"nome":"Produto","preco":-1}"""),
            (Produtos, """{"nome":"Produto","preco":0}"""),
            (Produtos, """{"nome":"Produto","preco":0.001}"""),
            (Produtos, """{"nome":"Produto","preco":1.001}"""),
            (Produtos, """{"nome":"Produto","preco":1.00000000000000000000000000001}"""),
            (Produtos, """{"nome":"Produto","preco":9999999999999999.9900000000000001}"""),
            (Produtos, """{"nome":"Produto","preco":"100000000000000000000000000001e-29"}"""),
            (Produtos, """{"nome":"Produto","preco":"1\uD800"}"""),
            (Produtos, """{"nome":"Produto","preco":10000000000000000}"""),
            (Produtos, """{"nome":"Produto","preco":79228162514264337593543950335}"""),
            (Produtos, """{"nome":"Produto","preco":79228162514264337593543950336}"""),
            (Produtos, ProdutoComProfundidade(33))
        };
        foreach (var (rota, corpo) in entradas)
        {
            using var response = await EnviarAsync(client, HttpMethod.Post, rota, corpo, fixture.OperatorToken);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Empty(banco.Executados);

        using var aceito = await EnviarAsync(client, HttpMethod.Post, Produtos, ProdutoComProfundidade(32), fixture.OperatorToken);
        Assert.Equal(HttpStatusCode.Created, aceito.StatusCode);
        var produto = Assert.IsType<ProdutoResponse>(await aceito.Content.ReadFromJsonAsync<ProdutoResponse>());
        Assert.Equal(0.01m, produto.Preco);

        static string ProdutoComProfundidade(int profundidade) =>
            """{"nome":"Produto","preco":"10\u0030e-4","extra":""" +
            new string('[', profundidade - 1) + "0" + new string(']', profundidade - 1) + "}";
    }

    [SqlServerFact]
    public async Task Listagens_RejeitamPaginacaoInvalidaNoHttpENosServicosSemExecutarSql()
    {
        var banco = new ComandosObservaveis();
        using var api = CriarApi(banco);
        using var client = CriarCliente(api);
        await using var context = fixture.Database.CreateContext(banco);
        var compradores = new CompradorService(context);
        var produtos = new ProdutoService(context);
        var pedidos = new PedidoService(context);
        banco.Executados.Clear();

        foreach (var (pagina, tamanho) in new[]
        {
            (0, 20), (-1, 20), (1, 0), (1, -1), (1, 51), (10_002, 1), (202, 50),
            (int.MinValue, 1), (int.MaxValue, 50), (int.MaxValue, int.MaxValue), (1_073_741_825, 4)
        })
        {
            var paginacao = new PaginacaoRequest { Pagina = pagina, TamanhoPagina = tamanho };
            var filtro = new PedidoFiltroRequest { Pagina = pagina, TamanhoPagina = tamanho };
            await Assert.ThrowsAsync<RegraDeNegocioException>(() => compradores.ListarAsync(paginacao));
            await Assert.ThrowsAsync<RegraDeNegocioException>(() => produtos.ListarAsync(paginacao));
            await Assert.ThrowsAsync<RegraDeNegocioException>(() => pedidos.ListarAsync(filtro));

            foreach (var rota in new[] { Compradores, Produtos, Pedidos })
            {
                using var response = await EnviarAsync(client, HttpMethod.Get,
                    $"{rota}?pagina={pagina}&tamanhoPagina={tamanho}", token: fixture.OperatorToken);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            }
        }

        foreach (var (rota, prefixo) in new[] { (Compradores, "paginacao"), (Produtos, "paginacao"), (Pedidos, "filtro") })
        {
            foreach (var parametro in new[] { "pagina", "tamanhoPagina" })
            {
                foreach (var valor in new[] { "", "abc", "2147483648" })
                {
                    using var response = await EnviarAsync(client, HttpMethod.Get,
                        $"{rota}?{parametro}={valor}", token: fixture.OperatorToken);
                    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                    Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
                }
            }

            using var respostaComPrefixo = await EnviarAsync(client, HttpMethod.Get,
                $"{rota}?pagina=abc&{prefixo}.pagina=1", token: fixture.OperatorToken);
            Assert.Equal(HttpStatusCode.BadRequest, respostaComPrefixo.StatusCode);
            Assert.Equal("application/problem+json", respostaComPrefixo.Content.Headers.ContentType?.MediaType);
        }

        Assert.Empty(banco.Executados);
    }

    [SqlServerFact]
    public async Task Pedidos_ValidamTodaAColecaoAntesDeConsultarEGravamQuantidadeEPrecoExtremos()
    {
        var banco = new ComandosObservaveis();
        using var api = CriarApi(banco);
        using var client = CriarCliente(api);
        var token = fixture.OperatorToken;
        using var compradorCriado = await EnviarAsync(client, HttpMethod.Post, Compradores,
            $$"""{"nome":"Comprador","email":"{{Guid.NewGuid():N}}@example.test"}""", token);
        Assert.Equal(HttpStatusCode.Created, compradorCriado.StatusCode);
        var comprador = Assert.IsType<CompradorResponse>(await compradorCriado.Content.ReadFromJsonAsync<CompradorResponse>());
        using var produtoCriado = await EnviarAsync(client, HttpMethod.Post, Produtos,
            """{"nome":"Produto de valor máximo","preco":9999999999999999.99}""", token);
        Assert.Equal(HttpStatusCode.Created, produtoCriado.StatusCode);
        var produto = Assert.IsType<ProdutoResponse>(await produtoCriado.Content.ReadFromJsonAsync<ProdutoResponse>());
        Assert.Equal(9_999_999_999_999_999.99m, produto.Preco);

        var itemValido = $$"""{"produtoId":"{{produto.Id}}","quantidade":2147483647}""";
        var itemSemId = """{"quantidade":1}""";
        var itemComIdZero = $$"""{"produtoId":"{{Guid.Empty}}","quantidade":1}""";
        var itemSemQuantidade = $$"""{"produtoId":"{{produto.Id}}"}""";
        var excesso = string.Join(",", Enumerable.Repeat(itemValido, 101));
        var colecoesInvalidas = new[] { "[]", "null", $"[{itemValido},null]", $"[{itemValido},{itemSemId}]",
            $"[{itemComIdZero}]", $"[{itemSemQuantidade}]", $"[{excesso}]" }
            .Concat(new[] { "0", "-1", "2147483648", "1.5" }.Select(quantidade =>
                $$"""[{"produtoId":"{{produto.Id}}","quantidade":{{quantidade}}}]"""));
        var corpoValido = $$"""{"compradorId":"{{comprador.Id}}","itens":[{{itemValido}}]}""";
        using var criado = await EnviarAsync(client, HttpMethod.Post, Pedidos, corpoValido, token);
        Assert.Equal(HttpStatusCode.Created, criado.StatusCode);
        var pedido = Assert.IsType<PedidoResponse>(await criado.Content.ReadFromJsonAsync<PedidoResponse>());
        const decimal subtotalExato = 21_474_836_469_999_999_978_525_163.53m;
        Assert.Equal(subtotalExato, Assert.Single(pedido.Itens).Subtotal);
        Assert.Equal(subtotalExato, pedido.ValorTotal);
        var rotaAtualizar = $"{Pedidos}/{pedido.Id}";
        banco.Executados.Clear();

        foreach (var itens in colecoesInvalidas)
        {
            using var criacao = await EnviarAsync(client, HttpMethod.Post, Pedidos,
                $$"""{"compradorId":"{{comprador.Id}}","itens":{{itens}}}""", token);
            using var atualizacao = await EnviarAsync(client, HttpMethod.Put, rotaAtualizar,
                $$"""{"itens":{{itens}}}""", token);
            Assert.Equal(HttpStatusCode.BadRequest, criacao.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, atualizacao.StatusCode);
        }
        using var semComprador = await EnviarAsync(client, HttpMethod.Post, Pedidos, $$"""{"itens":[{{itemValido}}]}""", token);
        using var semItens = await EnviarAsync(client, HttpMethod.Post, Pedidos, $$"""{"compradorId":"{{comprador.Id}}"}""", token);
        Assert.Equal(HttpStatusCode.BadRequest, semComprador.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, semItens.StatusCode);
        Assert.Empty(banco.Executados);

        await using var context = fixture.Database.CreateContext();
        var original = await context.Pedidos.AsNoTracking().Include(p => p.Itens).SingleAsync(p => p.CompradorId == comprador.Id);
        Assert.Equal(pedido.Id, original.Id);
        Assert.Null(original.DataAtualizacao);
        Assert.Equal(int.MaxValue, Assert.Single(original.Itens).Quantidade);

        var totalSemPrecisao = string.Join(",", Enumerable.Repeat(itemValido, 99));
        using var criacaoSemPrecisao = await EnviarAsync(client, HttpMethod.Post, Pedidos,
            $$"""{"compradorId":"{{comprador.Id}}","itens":[{{totalSemPrecisao}}]}""", token);
        using var atualizacaoSemPrecisao = await EnviarAsync(client, HttpMethod.Put, rotaAtualizar,
            $$"""{"itens":[{{totalSemPrecisao}}]}""", token);
        Assert.Equal(HttpStatusCode.BadRequest, criacaoSemPrecisao.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, atualizacaoSemPrecisao.StatusCode);
        Assert.Contains("precisão", JsonDocument.Parse(await criacaoSemPrecisao.Content.ReadAsStringAsync())
            .RootElement.GetProperty("mensagem").GetString());
        Assert.Equal(1, await context.Pedidos.CountAsync(p => p.CompradorId == comprador.Id));
        var preservado = await context.Pedidos.AsNoTracking().Include(p => p.Itens).SingleAsync(p => p.Id == pedido.Id);
        Assert.Null(preservado.DataAtualizacao);
        Assert.Equal(Assert.Single(original.Itens).Id, Assert.Single(preservado.Itens).Id);

        var noLimite = string.Join(",", Enumerable.Repeat(itemValido, 100));
        using var criacaoNoLimite = await EnviarAsync(client, HttpMethod.Post, Pedidos,
            $$"""{"compradorId":"{{comprador.Id}}","itens":[{{noLimite}}]}""", token);
        Assert.Equal(HttpStatusCode.Created, criacaoNoLimite.StatusCode);
        var pedidoNoLimite = Assert.IsType<PedidoResponse>(await criacaoNoLimite.Content.ReadFromJsonAsync<PedidoResponse>());
        Assert.Equal(100, pedidoNoLimite.Itens.Count);
        Assert.Equal(2_147_483_646_999_999_997_852_516_353m, pedidoNoLimite.ValorTotal);
        Assert.All(pedidoNoLimite.Itens, item => Assert.Equal(produto.Id, item.ProdutoId));

        using var atualizacaoNoLimite = await EnviarAsync(client, HttpMethod.Put, rotaAtualizar, $$"""{"itens":[{{noLimite}}]}""", token);
        Assert.Equal(HttpStatusCode.NoContent, atualizacaoNoLimite.StatusCode);
        Assert.Equal(100, await context.ItensPedido.CountAsync(item => item.PedidoId == pedido.Id));
    }

    [SqlServerFact]
    public async Task Swagger_DocumentaLimitesEFormatosDeEntrada()
    {
        using var swagger = JsonDocument.Parse(await fixture.Client.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = swagger.RootElement.GetProperty("paths");
        foreach (var (rota, metodo, teto) in new[]
        {
            (Login, "post", "16384"), (Cadastro, "post", "262144"), (Compradores, "post", "262144"),
            (Produtos, "post", "262144"), (Pedidos, "post", "262144"), ($"{Pedidos}/{{id}}", "put", "262144")
        })
        {
            var operacao = paths.GetProperty(rota).GetProperty(metodo);
            var respostas = operacao.GetProperty("responses");
            Assert.Contains(teto, respostas.GetProperty("413").GetProperty("description").GetString());
            Assert.True(respostas.TryGetProperty("400", out _));
            Assert.True(respostas.TryGetProperty("415", out _));
        }

        var schemas = swagger.RootElement.GetProperty("components").GetProperty("schemas");
        var login = schemas.GetProperty(nameof(LoginRequest));
        Assert.False(login.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(200, login.GetProperty("properties").GetProperty("email").GetProperty("maxLength").GetInt32());
        foreach (var modelo in new[] { nameof(CriarPedidoRequest), nameof(AtualizarPedidoRequest) })
        {
            var itens = schemas.GetProperty(modelo).GetProperty("properties").GetProperty("itens");
            Assert.Equal(1, itens.GetProperty("minItems").GetInt32());
            Assert.Equal(100, itens.GetProperty("maxItems").GetInt32());
        }
        var preco = schemas.GetProperty(nameof(CriarProdutoRequest)).GetProperty("properties").GetProperty("preco");
        Assert.Equal(9_999_999_999_999_999.99m, preco.GetProperty("maximum").GetDecimal());
        Assert.Equal(0.01m, preco.GetProperty("multipleOf").GetDecimal());

        foreach (var (rota, tamanhoPadrao) in new[] { (Compradores, 20), (Produtos, 20), (Pedidos, 10) })
        {
            var operacao = paths.GetProperty(rota).GetProperty("get");
            var parametros = operacao.GetProperty("parameters").EnumerateArray()
                .ToDictionary(parametro => parametro.GetProperty("name").GetString()!);
            var pagina = parametros["pagina"];
            Assert.Contains("10000", pagina.GetProperty("description").GetString());
            Assert.Equal(1, pagina.GetProperty("schema").GetProperty("default").GetInt32());
            Assert.Equal(1, pagina.GetProperty("schema").GetProperty("minimum").GetInt32());
            var tamanho = parametros["tamanhoPagina"].GetProperty("schema");
            Assert.Equal(1, tamanho.GetProperty("minimum").GetInt32());
            Assert.Equal(50, tamanho.GetProperty("maximum").GetInt32());
            Assert.Equal(tamanhoPadrao, tamanho.GetProperty("default").GetInt32());
            var respostas = operacao.GetProperty("responses");
            var respostaPaginada = respostas.GetProperty("200").GetProperty("content").GetProperty("application/json");
            var exemplo = respostaPaginada.GetProperty("example");
            Assert.Empty(exemplo.GetProperty("items").EnumerateArray());
            Assert.Equal(tamanhoPadrao, exemplo.GetProperty("tamanhoPagina").GetInt32());
            var schema = respostaPaginada.GetProperty("schema");
            var propriedades = schemas.GetProperty(schema.GetProperty("$ref").GetString()!.Split('/')[^1])
                .GetProperty("properties");
            Assert.Equal("array", propriedades.GetProperty("items").GetProperty("type").GetString());
            Assert.Equal("int64", propriedades.GetProperty("totalItens").GetProperty("format").GetString());
            Assert.Equal("int64", propriedades.GetProperty("totalPaginas").GetProperty("format").GetString());
            var erros = respostas.GetProperty("400").GetProperty("content");
            Assert.True(erros.TryGetProperty("application/json", out _));
            Assert.True(erros.TryGetProperty("application/problem+json", out _));
        }
    }

    private WebApplicationFactory<Program> CriarApi(
        ComandosObservaveis banco, AuthObservavel? auth = null, CapturaDeLogs? logs = null) =>
        fixture.Api.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddScoped<ApplicationDbContext>(_ => fixture.Database.CreateContext(banco));
                if (auth is not null)
                    services.AddSingleton<IAuthService>(auth);
            });
            if (logs is not null)
                builder.ConfigureLogging(logging => logging.AddFilter("Microsoft.AspNetCore", LogLevel.Trace).AddProvider(logs));
        });

    private static HttpClient CriarCliente(WebApplicationFactory<Program> api) => api.CreateClient(
        new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    private static async Task<HttpResponseMessage> EnviarAsync(HttpClient client, HttpMethod metodo, string rota,
        string? corpo = null, string? token = null, string tipo = "application/json")
    {
        using var request = new HttpRequestMessage(metodo, rota);
        if (corpo is not null)
            request.Content = new StringContent(corpo, Encoding.UTF8, tipo);
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private sealed class AuthObservavel : IAuthService
    {
        public ConcurrentQueue<LoginRequest> Logins { get; } = new();
        public int Cadastros;

        public Task<LoginResponse> LoginAsync(LoginRequest request)
        {
            Logins.Enqueue(request);
            throw new CredenciaisInvalidasException();
        }

        public Task RegistrarAsync(RegistrarUsuarioRequest request)
        {
            Interlocked.Increment(ref Cadastros);
            return Task.CompletedTask;
        }
    }

    private sealed class CapturaDeLogs : ILoggerProvider, ILogger
    {
        public ConcurrentQueue<string> Mensagens { get; } = new();
        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Mensagens.Enqueue($"{formatter(state, exception)} {exception}");
        public void Dispose() { }
    }
}
