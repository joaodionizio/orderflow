using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ecommerce.Application.Dtos.Responses;
using Ecommerce.Domain.Entities;
using Ecommerce.Domain.Enums;
using Ecommerce.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ecommerce.Tests;

public class PaginacaoTests(AutorizacaoApiFixture fixture) : IClassFixture<AutorizacaoApiFixture>
{
    private const string Compradores = "/api/v1/compradores";
    private const string Produtos = "/api/v1/produtos";
    private const string Pedidos = "/api/v1/pedidos";
    private const decimal PrecoMaximo = 9_999_999_999_999_999.99m;

    [Fact]
    public void TotalDePaginas_UsaContagemInteiraSemArredondamentoOuOverflow()
    {
        Assert.Equal(0L, ResultadoPaginadoResponse<int>.Criar([], 1, 50, 0).TotalPaginas);
        Assert.Equal(2L, ResultadoPaginadoResponse<int>.Criar([], 1, 50, 100).TotalPaginas);
        Assert.Equal(3L, ResultadoPaginadoResponse<int>.Criar([], 1, 50, 101).TotalPaginas);
        var extremo = ResultadoPaginadoResponse<int>.Criar([], 1, 50, long.MaxValue);
        Assert.Equal(long.MaxValue, extremo.TotalItens);
        Assert.Equal(184_467_440_737_095_517L, extremo.TotalPaginas);
        Assert.Equal(long.MaxValue, ResultadoPaginadoResponse<int>.Criar([], 1, 1, long.MaxValue).TotalPaginas);
    }

    [SqlServerFact]
    public async Task Listagens_PaginamNoBancoComOrdenacaoEstavelEFiltrosDePedidos()
    {
        var banco = new ComandosObservaveis();
        using var api = fixture.Api.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddScoped<ApplicationDbContext>(_ => fixture.Database.CreateContext(banco))));
        using var cliente = api.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.OperatorToken);

        var compradoresVazios = await LerPaginaAsync<CompradorResponse>(cliente, Compradores);
        VerificarPagina(compradoresVazios, 1, 20, 0, 0, x => x.Id);
        var produtosVazios = await LerPaginaAsync<ProdutoResponse>(cliente, Produtos);
        VerificarPagina(produtosVazios, 1, 20, 0, 0, x => x.Id);
        var pedidosVazios = await LerPaginaAsync<PedidoResponse>(cliente, Pedidos);
        VerificarPagina(pedidosVazios, 1, 10, 0, 0, x => x.Id);

        await using var contexto = fixture.Database.CreateContext();
        await SemearAsync(contexto);

        await VerificarCatalogoAsync<CompradorResponse>(cliente, banco, Compradores, "Compradores", x => x.Id);
        await VerificarCatalogoAsync<ProdutoResponse>(cliente, banco, Produtos, "Produtos", x => x.Id);
        await VerificarPedidosAsync(cliente, banco);

        var comprador = await cliente.GetFromJsonAsync<CompradorResponse>($"{Compradores}/{Id(2)}");
        Assert.Equal(Id(2), comprador?.Id);
        var produto = await cliente.GetFromJsonAsync<ProdutoResponse>($"{Produtos}/{Id(3)}");
        Assert.Equal(PrecoMaximo, produto?.Preco);
        var pedido = await cliente.GetFromJsonAsync<PedidoResponse>($"{Pedidos}/{Id(23)}");
        Assert.Equal(101, pedido?.Itens.Count);

        await contexto.Database.ExecuteSqlRawAsync("""
            INSERT dbo.Produtos (Id, Nome, Preco, DataCriacao)
            SELECT TOP (10000) NEWID(), N'ZZ', 0.01, '2026-01-01'
            FROM sys.all_objects AS a CROSS JOIN sys.all_objects AS b
            """);
        var ultimaPermitida = await LerPaginaAsync<ProdutoResponse>(cliente, $"{Produtos}?pagina=201&tamanhoPagina=50");
        Assert.Equal((201, 50, 10_051L, 202L), (ultimaPermitida.PaginaAtual, ultimaPermitida.TamanhoPagina,
            ultimaPermitida.TotalItens, ultimaPermitida.TotalPaginas));
        Assert.Equal(50, ultimaPermitida.Items.Count);
    }

    private static async Task VerificarCatalogoAsync<T>(
        HttpClient cliente, ComandosObservaveis banco, string rota, string tabela, Func<T, Guid> id)
    {
        var idsOrdenados = new[] { Id(2), Id(3), Id(1) }.Concat(Enumerable.Range(4, 48).Select(Id)).ToArray();
        var padrao = await LerPaginaAsync<T>(cliente, rota);
        VerificarPagina(padrao, 1, 20, 51, 3, id, idsOrdenados[..20]);

        var primeira = await LerPaginaAsync<T>(cliente, $"{rota}?pagina=1&tamanhoPagina=50");
        VerificarPagina(primeira, 1, 50, 51, 2, id, idsOrdenados[..50]);

        var ultima = await LerPaginaAsync<T>(cliente, $"{rota}?pagina=2&tamanhoPagina=50");
        VerificarPagina(ultima, 2, 50, 51, 2, id, idsOrdenados[50..]);

        banco.Executados.Clear();
        var desempate = await LerPaginaAsync<T>(cliente, $"{rota}?pagina=2&tamanhoPagina=1");
        VerificarPagina(desempate, 2, 1, 51, 51, id, Id(3));
        if (Assert.Single(desempate.Items) is ProdutoResponse produto)
            Assert.Equal(PrecoMaximo, produto.Preco);

        var sql = VerificarComandos(banco, tabela);
        Assert.DoesNotContain("JOIN", sql);
        Assert.Matches(@"ORDER BY \[\w+\]\.\[Nome\], \[\w+\]\.\[Id\]\s+OFFSET @\w+ ROWS FETCH NEXT @\w+ ROWS ONLY", sql);

        var semRegistros = await LerPaginaAsync<T>(cliente, $"{rota}?pagina=10001&tamanhoPagina=1");
        VerificarPagina(semRegistros, 10_001, 1, 51, 51, id);
    }

    private static async Task VerificarPedidosAsync(HttpClient cliente, ComandosObservaveis banco)
    {
        var padrao = await LerPaginaAsync<PedidoResponse>(cliente, Pedidos);
        VerificarPagina(padrao, 1, 10, 5, 1, x => x.Id, Id(17), Id(18), Id(23), Id(24), Id(1));
        var tamanhoMaximo = await LerPaginaAsync<PedidoResponse>(cliente, $"{Pedidos}?tamanhoPagina=50");
        VerificarPagina(tamanhoMaximo, 1, 50, 5, 1, x => x.Id, Id(17), Id(18), Id(23), Id(24), Id(1));

        var mesmoComprador = await LerPaginaAsync<PedidoResponse>(cliente, $"{Pedidos}?compradorId={Id(2)}");
        VerificarPagina(mesmoComprador, 1, 10, 4, 1, x => x.Id, Id(17), Id(23), Id(24), Id(1));
        var iniciados = await LerPaginaAsync<PedidoResponse>(cliente, $"{Pedidos}?status=Iniciado");
        VerificarPagina(iniciados, 1, 10, 4, 1, x => x.Id, Id(18), Id(23), Id(24), Id(1));

        var filtro = $"{Pedidos}?status=Iniciado&compradorId={Id(2)}";
        banco.Executados.Clear();
        var primeira = await LerPaginaAsync<PedidoResponse>(cliente, $"{filtro}&pagina=1&tamanhoPagina=2");
        VerificarPagina(primeira, 1, 2, 3, 2, x => x.Id, Id(23), Id(24));
        var sql = VerificarComandos(banco, "Pedidos");
        Assert.Contains("[Status]", banco.Executados.First());
        Assert.Contains("[CompradorId]", banco.Executados.First());
        Assert.Matches(@"ORDER BY \[\w+\]\.\[DataCriacao\] DESC, \[\w+\]\.\[Id\]\s+OFFSET @\w+ ROWS FETCH NEXT @\w+ ROWS ONLY", sql);
        Assert.True(sql.IndexOf("OFFSET", StringComparison.Ordinal) < sql.IndexOf("FROM [ItensPedido]", StringComparison.Ordinal));
        Assert.Contains("JOIN [Compradores]", sql);
        Assert.Contains("JOIN [Produtos]", sql);
        Assert.DoesNotContain("[Preco]", sql);

        var legado = primeira.Items[0];
        Assert.Equal("A", legado.NomeComprador);
        Assert.Equal("2@example.test", legado.EmailComprador);
        Assert.Equal("Iniciado", legado.Status);
        Assert.Equal(101, legado.Itens.Count);
        var item = Assert.Single(legado.Itens, item => item.Quantidade == int.MaxValue);
        Assert.Equal(Id(2), item.ProdutoId);
        Assert.Equal("A", item.NomeProduto);
        Assert.Equal(PrecoMaximo, item.PrecoUnitario);
        Assert.Equal(21_474_836_469_999_999_978_525_163.53m, item.Subtotal);
        Assert.Equal(21_474_836_469_999_999_978_525_164.53m, legado.ValorTotal);

        var ultima = await LerPaginaAsync<PedidoResponse>(cliente, $"{filtro}&pagina=2&tamanhoPagina=2");
        VerificarPagina(ultima, 2, 2, 3, 2, x => x.Id, Id(1));
        var desempate = await LerPaginaAsync<PedidoResponse>(cliente, $"{filtro}&pagina=2&tamanhoPagina=1");
        VerificarPagina(desempate, 2, 1, 3, 3, x => x.Id, Id(24));
        var semRegistros = await LerPaginaAsync<PedidoResponse>(cliente, $"{filtro}&pagina=5001&tamanhoPagina=2");
        VerificarPagina(semRegistros, 5_001, 2, 3, 2, x => x.Id);
    }

    private static string VerificarComandos(ComandosObservaveis banco, string tabela)
    {
        var comandos = banco.Executados.ToArray();
        Assert.Equal(2, comandos.Length);
        Assert.StartsWith("SELECT COUNT_BIG(*)", comandos[0]);
        Assert.Contains($"FROM [{tabela}]", comandos[0]);
        Assert.DoesNotContain("JOIN", comandos[0]);
        Assert.DoesNotContain("ORDER BY", comandos[0]);
        Assert.Contains($"FROM [{tabela}]", comandos[1]);
        return comandos[1];
    }

    private static async Task<ResultadoPaginadoResponse<T>> LerPaginaAsync<T>(HttpClient cliente, string rota)
    {
        using var resposta = await cliente.GetAsync(rota);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        return Assert.IsType<ResultadoPaginadoResponse<T>>(
            await resposta.Content.ReadFromJsonAsync<ResultadoPaginadoResponse<T>>());
    }

    private static void VerificarPagina<T>(ResultadoPaginadoResponse<T> pagina, int atual,
        int tamanho, long totalItens, long totalPaginas, Func<T, Guid> id, params Guid[] ids)
    {
        Assert.Equal((atual, tamanho, totalItens, totalPaginas),
            (pagina.PaginaAtual, pagina.TamanhoPagina, pagina.TotalItens, pagina.TotalPaginas));
        Assert.Equal(ids, pagina.Items.Select(id));
    }

    private static async Task SemearAsync(ApplicationDbContext contexto)
    {
        var data = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (var numero in Enumerable.Range(1, 51).Reverse())
        {
            var nome = numero switch { 1 => "B", 2 or 3 => "A", _ => "Z" };
            contexto.Compradores.Add(new Comprador
            {
                Id = Id(numero), Nome = nome, Email = $"{numero}@example.test", DataCriacao = data
            });
            contexto.Produtos.Add(new Produto
            {
                Id = Id(numero), Nome = nome, Preco = numero == 3 ? PrecoMaximo : 0.01m, DataCriacao = data
            });
        }

        var legado = new Pedido { Id = Id(23), CompradorId = Id(2), DataCriacao = data };
        legado.Itens.Add(new ItemPedido
        {
            Id = Id(1), ProdutoId = Id(2), PrecoUnitario = PrecoMaximo, Quantidade = int.MaxValue
        });
        foreach (var numero in Enumerable.Range(2, 100))
        {
            legado.Itens.Add(new ItemPedido
            {
                Id = Id(numero), ProdutoId = Id(3), PrecoUnitario = 0.01m, Quantidade = 1
            });
        }

        contexto.Pedidos.AddRange(
            new Pedido { Id = Id(24), CompradorId = Id(2), DataCriacao = data },
            legado,
            new Pedido { Id = Id(18), CompradorId = Id(3), DataCriacao = data },
            new Pedido { Id = Id(17), CompradorId = Id(2), DataCriacao = data, Status = StatusPedido.Processado },
            new Pedido { Id = Id(1), CompradorId = Id(2), DataCriacao = data.AddDays(-1) });
        await contexto.SaveChangesAsync();
    }

    private static Guid Id(int numero) => new($"00000000-0000-0000-0000-{numero:D12}");
}
